using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Caching.Distributed;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>Результат агрегації + звідки він (кеш чи свіжа збірка) — для заголовка X-Cache.</summary>
public sealed record AggregationResult<T>(T Value, bool FromCache, long ElapsedMs);

/// <summary>
/// Aggregator Pattern. Аналогія: офіціант, який замість трьох походів (на кухню, до бару, до каси)
/// одночасно відправляє три записки й приносить усе на одній таці. Якщо бар зачинений — несе їжу
/// й чек, а на таці записка «напої тимчасово недоступні» (Warnings), а не повертається з порожніми руками (500).
/// Лаб. 06: записки тепер ідуть gRPC (одне HTTP/2-з'єднання на кухню), а готову тацю на 30–60 с
/// ставимо на «полицю видачі» (Redis), щоб наступний такий самий запит не смикав кухні взагалі.
/// </summary>
public sealed class AggregationService(
    GrpcPlatformClient grpc,
    HttpPlatformClient http,
    IDistributedCache cache,
    AggregatorMetrics metrics,
    ILogger<AggregationService> logger)
{
    public static readonly ActivitySource ActivitySource = new("SmakoViz.Aggregator");

    private const int LatestReviewsCount = 3;
    private const string DeliveryService = "delivery-api";
    private const string MenuService = "menu-api";
    private const string ReviewsService = "reviews-api";

    // Агрегований кеш живе КОРОТШЕ за кеші сервісів (замовлення 60 с, меню 10 хв),
    // інакше зібрана відповідь виглядала б «свіжішою» за дані, з яких її зібрано.
    private static readonly TimeSpan FullOrderTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RestaurantTtl = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string FullOrderKey(long orderId) => $"aggregator:order:{orderId}:full";
    public static string RestaurantKey(long restaurantId) => $"aggregator:restaurant:{restaurantId}";

    // ------------------------------------------------------------------ публічні сценарії (з кешем Redis)

    public Task<AggregationResult<FullOrderDto>?> GetFullOrderAsync(long orderId, Transport transport, bool useCache, CancellationToken ct)
        => WithCacheAsync(FullOrderKey(orderId), FullOrderTtl, "full-order", transport, useCache,
            client => ComposeFullOrderAsync(client, orderId, ct), r => r.Warnings.Count == 0, ct);

    public Task<AggregationResult<RestaurantOverviewDto>?> GetRestaurantOverviewAsync(long restaurantId, Transport transport,
        bool useCache, CancellationToken ct)
        => WithCacheAsync(RestaurantKey(restaurantId), RestaurantTtl, "restaurant-overview", transport, useCache,
            client => ComposeRestaurantOverviewAsync(client, restaurantId, ct), r => r.Warnings.Count == 0, ct);

    /// <summary>
    /// Cache-aside для агрегованої відповіді: ключ із параметрів запиту, перевірка ДО паралельних викликів,
    /// часткові відповіді (є Warnings) НЕ кешуються. Недоступний Redis не ламає запит — просто збираємо заново.
    /// </summary>
    private async Task<AggregationResult<T>?> WithCacheAsync<T>(string key, TimeSpan ttl, string scenario, Transport transport,
        bool useCache, Func<IPlatformClient, Task<T?>> compose, Func<T, bool> cacheable, CancellationToken ct) where T : class
    {
        var sw = Stopwatch.StartNew();

        if (useCache && await TryGetCachedAsync<T>(key, ct) is { } cached)
        {
            metrics.CacheHit();
            metrics.RecordRequest(scenario, transport, fromCache: true, sw.Elapsed.TotalMilliseconds);
            logger.LogInformation("Aggregator cache hit {CacheKey}", key);
            return new AggregationResult<T>(cached, true, sw.ElapsedMilliseconds);
        }
        if (useCache)
        {
            metrics.CacheMiss();
            logger.LogInformation("Aggregator cache miss {CacheKey}", key);
        }

        var client = transport == Transport.Http ? (IPlatformClient)http : grpc;
        var result = await compose(client);
        sw.Stop();
        metrics.RecordRequest(scenario, transport, fromCache: false, sw.Elapsed.TotalMilliseconds);
        logger.LogInformation("Aggregated {Scenario} via {Transport} in {ElapsedMs} ms", scenario, transport, sw.ElapsedMilliseconds);

        if (result is null)
            return null;

        if (useCache && cacheable(result))
            await TrySetCachedAsync(key, result, ttl, ct);
        else if (useCache)
            logger.LogInformation("Partial response for {CacheKey} is not cached", key);

        return new AggregationResult<T>(result, false, sw.ElapsedMilliseconds);
    }

    private async Task<T?> TryGetCachedAsync<T>(string key, CancellationToken ct) where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, ct).WaitAsync(TimeSpan.FromSeconds(1), ct);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Redis unavailable while reading {CacheKey}; composing without cache", key);
            return null;
        }
    }

    private async Task TrySetCachedAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct).WaitAsync(TimeSpan.FromSeconds(1), ct);
            logger.LogInformation("Aggregator cached {CacheKey} for {Ttl}", key, ttl);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Redis unavailable while writing {CacheKey}", key);
        }
    }

    // ------------------------------------------------------------------ композиція

    /// <summary>
    /// Повна картка замовлення: замовлення (delivery-api) → ПАРАЛЕЛЬНО ресторан з актуальним меню (menu-api),
    /// рейтинг ресторану і відгуки цього клієнта (reviews-api). null — замовлення не знайдене.
    /// </summary>
    private async Task<FullOrderDto?> ComposeFullOrderAsync(IPlatformClient client, long orderId, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("AggregateFullOrder");
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("aggregator.transport", client.Transport.ToString());

        var warnings = new List<string>();

        // 1) замовлення — обов'язкове; якщо delivery-api недоступний → 503
        var order = await SafeAsync(DeliveryService, $"order {orderId}", () => client.GetOrderAsync(orderId, ct), warnings, ct);
        if (order is null)
        {
            if (warnings.Count > 0) throw new DependencyUnavailableException(DeliveryService);
            return null;
        }

        // 2) незалежні виклики — одночасно (Task.WhenAll), CancellationToken у кожному
        var restaurantTask = SafeAsync(MenuService, $"restaurant {order.RestaurantId}",
            () => client.GetRestaurantAsync(order.RestaurantId, onlyAvailable: false, ct), warnings, ct);
        var ratingTask = SafeAsync(ReviewsService, $"rating of restaurant {order.RestaurantId}",
            () => client.GetRestaurantRatingAsync(order.RestaurantId, ct), warnings, ct);
        var reviewsTask = SafeAsync(ReviewsService, $"reviews of customer {order.CustomerId}",
            () => client.GetCustomerReviewsAsync(order.RestaurantId, order.CustomerId, ct), warnings, ct);

        await Task.WhenAll(restaurantTask, ratingTask, reviewsTask);

        var restaurant = await restaurantTask;
        var rating = await ratingTask;
        var customerReviews = await reviewsTask ?? [];

        // 3) узгодженість: лише страви ЦЬОГО замовлення і відгуки саме на них / на цей ресторан
        var orderDishIds = order.Lines.Select(l => l.DishId).ToHashSet();
        var dishes = restaurant?.Dishes.Where(d => orderDishIds.Contains(d.Id)).ToDictionary(d => d.Id)
                     ?? new Dictionary<long, MenuDishDto>();
        var relevantReviews = customerReviews
            .Where(r => r.RestaurantId == order.RestaurantId && (r.DishId is null || orderDishIds.Contains(r.DishId.Value)))
            .ToList();

        if (restaurant is not null && restaurant.Id != order.RestaurantId)
            lock (warnings) warnings.Add($"{MenuService} returned restaurant {restaurant.Id} instead of {order.RestaurantId}");

        var items = order.Lines.Select(line =>
        {
            dishes.TryGetValue(line.DishId, out var dish);
            var dishRatings = relevantReviews.Where(r => r.DishId == line.DishId).Select(r => r.Rating).ToList();
            return new OrderItemCardDto(
                line.DishId, line.DishName, line.Quantity, line.UnitPrice,
                CurrentPrice: dish?.Price,
                AvailableNow: dish?.IsAvailable,
                DishRating: dishRatings.Count > 0 ? dishRatings.Average() : null);
        }).ToList();

        logger.LogInformation(
            "Aggregated order {OrderId}: {ItemsCount} items, {ReviewsCount} reviews, {WarningsCount} warnings",
            orderId, items.Count, relevantReviews.Count, warnings.Count);
        activity?.SetTag("aggregator.warnings", warnings.Count);

        return new FullOrderDto(
            new OrderCardDto(order.Id, order.OrderNumber, order.CustomerId, order.Status, order.PlacedAt,
                order.ItemsTotal, order.DeliveryFee, order.TotalAmount),
            restaurant is null && rating is null ? null : ToRestaurantCard(order.RestaurantId, order.RestaurantName, restaurant, rating),
            items,
            relevantReviews.Select(ToReviewCard).ToList(),
            warnings);
    }

    /// <summary>Картка ресторану: меню + рейтинг + останні 3 відгуки — три виклики паралельно. null — ресторану немає.</summary>
    private async Task<RestaurantOverviewDto?> ComposeRestaurantOverviewAsync(IPlatformClient client, long restaurantId, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("AggregateRestaurantOverview");
        activity?.SetTag("restaurant.id", restaurantId);
        activity?.SetTag("aggregator.transport", client.Transport.ToString());

        var warnings = new List<string>();

        var restaurantTask = SafeAsync(MenuService, $"restaurant {restaurantId}",
            () => client.GetRestaurantAsync(restaurantId, onlyAvailable: true, ct), warnings, ct);
        var ratingTask = SafeAsync(ReviewsService, $"rating of restaurant {restaurantId}",
            () => client.GetRestaurantRatingAsync(restaurantId, ct), warnings, ct);
        var latestTask = SafeAsync(ReviewsService, $"latest reviews of restaurant {restaurantId}",
            () => client.GetLatestReviewsAsync(restaurantId, LatestReviewsCount, ct), warnings, ct);

        await Task.WhenAll(restaurantTask, ratingTask, latestTask);

        var restaurant = await restaurantTask;
        var rating = await ratingTask;
        var latest = await latestTask ?? [];

        if (restaurant is null)
        {
            // menu-api живий, але ресторану немає → 404; menu-api недоступний → 503
            if (warnings.Any(w => w.StartsWith(MenuService)))
                throw new DependencyUnavailableException(MenuService);
            return null;
        }

        logger.LogInformation("Aggregated restaurant {RestaurantId}: {DishesCount} dishes, {ReviewsCount} reviews, {WarningsCount} warnings",
            restaurantId, restaurant.Dishes.Count, latest.Count, warnings.Count);

        return new RestaurantOverviewDto(
            ToRestaurantCard(restaurantId, restaurant.Name, restaurant, rating),
            restaurant.Dishes,
            latest.Where(r => r.RestaurantId == restaurantId).Select(ToReviewCard).ToList(),
            warnings);
    }

    // ------------------------------------------------------------------ виклики з логуванням і частковими відмовами

    /// <summary>
    /// Виклик сервісу з логуванням тривалості. Недоступність НЕ валить запит: повертається default,
    /// а в warnings додається повідомлення. gRPC: RpcException Unavailable / DeadlineExceeded (а також Internal,
    /// Unknown, ResourceExhausted — сервіс зламався); HTTP: HttpRequestException, таймаут, circuit breaker.
    /// Скасування самим клієнтом (ct) не ковтається.
    /// </summary>
    private async Task<T?> SafeAsync<T>(string service, string what, Func<Task<T?>> call, List<string> warnings, CancellationToken ct)
    {
        logger.LogInformation("Calling {Service} for {What}", service, what);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await call();
            logger.LogInformation("{Service} responded in {ElapsedMs} ms for {What}", service, sw.ElapsedMilliseconds, what);
            return result;
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested && ex.StatusCode is StatusCode.Unavailable
                                          or StatusCode.DeadlineExceeded or StatusCode.Internal
                                          or StatusCode.Unknown or StatusCode.ResourceExhausted or StatusCode.Cancelled)
        {
            return Degrade(ex, $"{ex.StatusCode}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException
                                       or TimeoutException
                                       or JsonException
                                       or Polly.ExecutionRejectedException   // таймаут / розімкнений circuit breaker
                                       or TaskCanceledException)
        {
            return Degrade(ex, ex.GetType().Name);
        }

        T? Degrade(Exception ex, string reason)
        {
            logger.LogWarning(ex, "{Service} is unavailable for {What} after {ElapsedMs} ms: {Reason}",
                service, what, sw.ElapsedMilliseconds, reason);
            metrics.PartialFailure(service);
            lock (warnings) warnings.Add($"{service} is unavailable: {what} not loaded");
            return default;
        }
    }

    // ------------------------------------------------------------------ мапінг у бізнес-DTO

    private static RestaurantCardDto ToRestaurantCard(long id, string fallbackName, MenuRestaurantDto? restaurant, ReviewsRatingDto? rating)
        => new(
            id,
            restaurant?.Name ?? fallbackName,
            restaurant?.City ?? "",
            restaurant?.CuisineType ?? "",
            restaurant?.IsActive ?? true,
            restaurant?.Profile?.Address,
            restaurant?.Profile?.Phone,
            restaurant?.Profile is { } p ? $"{p.OpensAt:HH\\:mm}–{p.ClosesAt:HH\\:mm}" : null,
            rating is { ReviewsCount: > 0 } ? Math.Round(rating.AverageRating, 2) : null,
            rating?.ReviewsCount);

    private static ReviewCardDto ToReviewCard(ReviewItemDto r)
        => new(r.Id, r.TargetType, r.DishName, r.AuthorName, r.Rating, r.Text, r.CreatedAt);
}

/// <summary>
/// Метрики Aggregator: тривалість агрегації з тегами transport (grpc/http) і cache (hit/miss) — саме за нею
/// порівнюється HTTP (Лаб. 05) і gRPC (Лаб. 06); cache.hits / cache.misses рівня L2; часткові відмови.
/// </summary>
public sealed class AggregatorMetrics
{
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _partialFailures;

    public AggregatorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("SmakoViz.Aggregator");
        _duration = meter.CreateHistogram<double>("aggregator.request.duration", unit: "ms",
            description: "Aggregation duration by scenario, transport and cache");
        var cacheMeter = meterFactory.Create(Microsoft.Extensions.Hosting.Extensions.CacheMeterName);
        _hits = cacheMeter.CreateCounter<long>("cache.hits");
        _misses = cacheMeter.CreateCounter<long>("cache.misses");
        _partialFailures = meter.CreateCounter<long>("aggregator.partial_failures", description: "Upstream calls turned into warnings");
    }

    public void RecordRequest(string scenario, Transport transport, bool fromCache, double ms)
        => _duration.Record(ms,
            new KeyValuePair<string, object?>("aggregator.scenario", scenario),
            new KeyValuePair<string, object?>("aggregator.transport", transport.ToString().ToLowerInvariant()),
            new KeyValuePair<string, object?>("aggregator.cache", fromCache ? "hit" : "miss"));

    public void CacheHit() => _hits.Add(1, new KeyValuePair<string, object?>("cache.level", "L2"));
    public void CacheMiss() => _misses.Add(1, new KeyValuePair<string, object?>("cache.level", "L2"));
    public void PartialFailure(string service) => _partialFailures.Add(1, new KeyValuePair<string, object?>("service", service));
}

/// <summary>Обов'язковий для відповіді сервіс недоступний → 503 ProblemDetails.</summary>
public sealed class DependencyUnavailableException(string service)
    : Exception($"{service} is unavailable")
{
    public string Service { get; } = service;
}
