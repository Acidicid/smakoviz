using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using Reviews.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Reviews.WebApi.Infrastructure;

/// <summary>Health check MongoDB: найлегша команда { ping: 1 }. Реєстрація: ім'я "reviews-db", теги database / mongodb / ready, таймаут 5 с.</summary>
public sealed class MongoHealthCheck(MongoDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken ct = default)
    {
        try
        {
            await context.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
            return HealthCheckResult.Healthy("MongoDB is reachable");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(healthContext.Registration.FailureStatus, "MongoDB is unreachable", ex);
        }
    }
}

/// <summary>Redis: PING. Кеш необов'язковий → failureStatus = Degraded (сервіс читає з MongoDB).</summary>
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
                "Redis is unavailable — cache disabled, reading from MongoDB", ex);
        }
    }
}
