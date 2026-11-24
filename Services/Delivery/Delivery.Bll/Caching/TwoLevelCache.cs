using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Delivery.Bll.Caching;

/// <summary>
/// Політика кешування одного типу даних. L1 (пам'ять процесу) ЗАВЖДИ живе коротше за L2 (Redis):
/// інакше застаріле значення в пам'яті переживе оновлення в Redis.
/// </summary>
/// <param name="L1Absolute">Абсолютний TTL у пам'яті.</param>
/// <param name="L1Sliding">Sliding expiration у пам'яті (для часто запитуваних даних) — продовжується при кожному зверненні, але не довше за L1Absolute.</param>
/// <param name="L2Absolute">Абсолютний TTL у Redis.</param>
/// <param name="Priority">Пріоритет при витісненні з пам'яті, коли досягнуто SizeLimit.</param>
public sealed record CachePolicy(TimeSpan L1Absolute, TimeSpan? L1Sliding, TimeSpan L2Absolute,
    CacheItemPriority Priority = CacheItemPriority.Normal)
{
    /// <summary>Довідники, що рідко змінюються (категорії): absolute expiration, високий пріоритет.</summary>
    public static readonly CachePolicy Reference = new(TimeSpan.FromMinutes(5), null, TimeSpan.FromHours(1), CacheItemPriority.High);

    /// <summary>Картки, які часто читають (страва, ресторан, відгук): sliding у пам'яті + absolute.</summary>
    public static readonly CachePolicy Card = new(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(10));

    /// <summary>Дані, що часто змінюються або містять персональні поля (замовлення): лише короткі TTL.</summary>
    public static readonly CachePolicy Volatile = new(TimeSpan.FromSeconds(15), null, TimeSpan.FromSeconds(60), CacheItemPriority.Low);
}

/// <summary>Дворівневий кеш L1 (IMemoryCache) → L2 (Redis, IDistributedCache) → джерело (БД).</summary>
public interface ITwoLevelCache
{
    /// <summary>Cache-aside: L1 → L2 → factory (БД); рівні заповнюються на зворотному шляху. null не кешується.</summary>
    Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T?>> factory, CachePolicy policy, CancellationToken ct);

    /// <summary>Інвалідація ОДРАЗУ після запису (синхронно з операцією) в обох рівнях; причина логується.</summary>
    Task InvalidateAsync(string reason, CancellationToken ct, params string[] keys);
}

/// <summary>
/// Аналогія: L1 — шухляда твого столу (мить, але лише твоя), L2 — спільна шафа в офісі (Redis: трохи далі,
/// зате однакова для всіх реплік сервісу), БД — архів у підвалі. Спершу шухляда, потім шафа, потім архів;
/// знайдене в архіві кладемо і в шафу, і в шухляду.
/// Якщо Redis недоступний — працюємо далі на L1 + БД (повільніше, але без помилок) і 30 с не смикаємо Redis.
/// </summary>
public sealed class TwoLevelCache(
    IMemoryCache memory,
    IDistributedCache distributed,
    CacheMetrics metrics,
    ILogger<TwoLevelCache> logger) : ITwoLevelCache
{
    private static readonly TimeSpan L2Timeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan L2RetryAfter = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private long _l2DisabledUntilTicks;

    /// <summary>Чи доступний зараз Redis (для health check).</summary>
    public bool L2Available => DateTime.UtcNow.Ticks >= Interlocked.Read(ref _l2DisabledUntilTicks);

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T?>> factory, CachePolicy policy,
        CancellationToken ct)
    {
        // ---------- L1: пам'ять процесу ----------
        if (memory.TryGetValue(key, out T? value) && value is not null)
        {
            metrics.Hit(CacheMetrics.L1);
            logger.LogDebug("Cache hit {CacheKey} at {CacheLevel}", key, CacheMetrics.L1);
            return value;
        }
        metrics.Miss(CacheMetrics.L1);

        // ---------- L2: Redis ----------
        var bytes = await TryL2Async(() => distributed.GetAsync(key, ct), key, "get", ct);
        if (bytes is not null)
        {
            value = JsonSerializer.Deserialize<T>(bytes, Json);
            if (value is not null)
            {
                metrics.Hit(CacheMetrics.L2);
                logger.LogInformation("Cache hit {CacheKey} at {CacheLevel}, {SizeBytes} bytes, L1 TTL {L1Ttl}",
                    key, CacheMetrics.L2, bytes.Length, policy.L1Absolute);
                SetL1(key, value, policy);   // заповнюємо L1 на зворотному шляху
                return value;
            }
        }
        metrics.Miss(CacheMetrics.L2);
        logger.LogInformation("Cache miss {CacheKey} — loading from database", key);

        // ---------- джерело (БД) ----------
        var sw = Stopwatch.StartNew();
        value = await factory(ct);
        if (value is null)
            return default;   // «не знайдено» не кешуємо

        var payload = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        await TryL2Async(async () =>
        {
            await distributed.SetAsync(key, payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = policy.L2Absolute }, ct);
            return Array.Empty<byte>();
        }, key, "set", ct);
        SetL1(key, value, policy);

        metrics.RecordLoad(sw.Elapsed.TotalMilliseconds);
        logger.LogInformation("Cached {CacheKey}: {SizeBytes} bytes, L1 TTL {L1Ttl}, L2 TTL {L2Ttl}, loaded in {ElapsedMs} ms",
            key, payload.Length, policy.L1Absolute, policy.L2Absolute, sw.ElapsedMilliseconds);
        return value;
    }

    public async Task InvalidateAsync(string reason, CancellationToken ct, params string[] keys)
    {
        foreach (var key in keys.Distinct())
        {
            memory.Remove(key);
            await TryL2Async(async () =>
            {
                await distributed.RemoveAsync(key, ct);
                return Array.Empty<byte>();
            }, key, "remove", ct);
            metrics.Invalidated(reason);
        }

        logger.LogInformation("Cache invalidated {CacheKeys}, reason {Reason}", keys, reason);
    }

    private void SetL1<T>(string key, T value, CachePolicy policy)
    {
        var options = new MemoryCacheEntryOptions
        {
            Size = 1,                                              // 1 «одиниця» з SizeLimit (Program.cs)
            AbsoluteExpirationRelativeToNow = policy.L1Absolute,   // L1 коротший за L2
            SlidingExpiration = policy.L1Sliding,
            Priority = policy.Priority
        };
        options.RegisterPostEvictionCallback((evictedKey, _, reason, _) =>
        {
            if (reason is EvictionReason.Capacity or EvictionReason.Expired)
                metrics.Evicted(reason.ToString());
        });
        memory.Set(key, value, options);
    }

    /// <summary>Будь-яка помилка Redis (недоступний, таймаут) → лог + пропуск L2 на 30 с. Скасування клієнтом не ковтаємо.</summary>
    private async Task<byte[]?> TryL2Async(Func<Task<byte[]?>> operation, string key, string op, CancellationToken ct)
    {
        if (!L2Available)
            return null;

        try
        {
            return await operation().WaitAsync(L2Timeout, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _l2DisabledUntilTicks, DateTime.UtcNow.Add(L2RetryAfter).Ticks);
            logger.LogWarning(ex, "Redis (L2) {Operation} failed for {CacheKey}; using L1 + database for {RetryAfter}",
                op, key, L2RetryAfter);
            return null;
        }
    }
}

/// <summary>
/// Метрики кешу (Meter "SmakoViz.Cache" підключено в ServiceDefaults): cache.hits / cache.misses з тегом
/// cache.level = L1 | L2, cache.invalidations (з причиною), cache.evictions, cache.load.duration.
/// Hit ratio = hits / (hits + misses) — рахується в Dashboard → Metrics.
/// </summary>
public sealed class CacheMetrics
{
    public const string MeterName = "SmakoViz.Cache";
    public const string L1 = "L1";
    public const string L2 = "L2";

    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _invalidations;
    private readonly Counter<long> _evictions;
    private readonly Histogram<double> _loadDuration;
    private long _totalHits, _totalMisses;

    public CacheMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _hits = meter.CreateCounter<long>("cache.hits", description: "Cache hits by level (L1 memory, L2 Redis)");
        _misses = meter.CreateCounter<long>("cache.misses", description: "Cache misses by level");
        _invalidations = meter.CreateCounter<long>("cache.invalidations", description: "Keys removed after writes");
        _evictions = meter.CreateCounter<long>("cache.evictions", description: "L1 entries evicted (capacity / expired)");
        _loadDuration = meter.CreateHistogram<double>("cache.load.duration", unit: "ms", description: "Time to load a missing value from the database");
    }

    public long TotalHits => Interlocked.Read(ref _totalHits);
    public long TotalMisses => Interlocked.Read(ref _totalMisses);

    public void Hit(string level)
    {
        Interlocked.Increment(ref _totalHits);
        _hits.Add(1, new KeyValuePair<string, object?>("cache.level", level));
    }

    public void Miss(string level)
    {
        if (level == L2) Interlocked.Increment(ref _totalMisses);   // справжній промах = дійшли до БД
        _misses.Add(1, new KeyValuePair<string, object?>("cache.level", level));
    }

    public void Invalidated(string reason) => _invalidations.Add(1, new KeyValuePair<string, object?>("cache.reason", reason));
    public void Evicted(string reason) => _evictions.Add(1, new KeyValuePair<string, object?>("cache.reason", reason));
    public void RecordLoad(double ms) => _loadDuration.Record(ms);
}

/// <summary>Статистика кешу в /health: кількість записів L1, hit ratio, чи доступний Redis. Redis down → Degraded, не Unhealthy.</summary>
public sealed class CacheHealthCheck(IMemoryCache memory, ITwoLevelCache cache, CacheMetrics metrics) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var stats = (memory as MemoryCache)?.GetCurrentStatistics();
        var hits = metrics.TotalHits;
        var misses = metrics.TotalMisses;
        var l2Available = (cache as TwoLevelCache)?.L2Available ?? true;

        var data = new Dictionary<string, object>
        {
            ["l1.entries"] = stats?.CurrentEntryCount ?? -1,
            ["l1.size"] = stats?.CurrentEstimatedSize ?? -1,
            ["hits"] = hits,
            ["misses"] = misses,
            ["hitRatio"] = hits + misses == 0 ? 0 : Math.Round((double)hits / (hits + misses), 3),
            ["l2.available"] = l2Available
        };

        return Task.FromResult(l2Available
            ? HealthCheckResult.Healthy("Cache L1 + L2 OK", data)
            : HealthCheckResult.Degraded("Redis (L2) unavailable — serving from L1 + database", data: data));
    }
}
