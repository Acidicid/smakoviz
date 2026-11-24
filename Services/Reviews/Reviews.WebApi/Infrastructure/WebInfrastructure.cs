using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using Reviews.Infrastructure.Persistence;
using Serilog.Context;

namespace Reviews.WebApi.Infrastructure;

/// <summary>Health check MongoDB: команда { ping: 1 }. Доступно на /health (детальніше — Лаб. 07).</summary>
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
            return HealthCheckResult.Unhealthy("MongoDB is unreachable", ex);
        }
    }
}

/// <summary>CorrelationId із заголовка X-Correlation-Id (або TraceIdentifier) — у кожен рядок логу запиту.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : context.TraceIdentifier;

        context.Response.Headers[HeaderName] = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
