using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Platform.Aggregator.HealthChecks;

/// <summary>
/// Redis: команда PING через IConnectionMultiplexer (його реєструє Aspire-інтеграція). Кеш — НЕОБОВ'ЯЗКОВА
/// залежність, тому failureStatus = Degraded: без Redis сервіс читає з БД і лишається доступним.
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync().WaitAsync(ct);
            return HealthCheckResult.Healthy($"Redis PONG in {latency.TotalMilliseconds:F1} ms");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                "Redis is unavailable — cache disabled, composing responses without cache", ex);
        }
    }
}

