using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Aggregator.HealthChecks;

/// <summary>
/// Перевірка gRPC-залежності за стандартним gRPC Health Checking Protocol: grpc.health.v1.Health/Check
/// (сервіси публікують його через MapGrpcHealthChecksService). Короткий deadline 2 с; без побічних ефектів.
/// Іменований клієнт Health.HealthClient (адреса https://&lt;service&gt;) — без retry: перевірка має бути швидкою.
/// Результат при збої — failureStatus реєстрації: delivery-api → Unhealthy (критичний), menu/reviews → Degraded.
/// </summary>
public sealed class GrpcDependencyHealthCheck(
    GrpcClientFactory clientFactory,
    string service,
    ILogger<GrpcDependencyHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var client = clientFactory.CreateClient<Health.HealthClient>(service);
        try
        {
            var reply = await client.CheckAsync(new HealthCheckRequest(),
                deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: ct);

            return reply.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"{service} is serving")
                : new HealthCheckResult(context.Registration.FailureStatus, $"{service} status: {reply.Status}");
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "{Service} health check failed: {GrpcStatus}", service, ex.StatusCode);
            return new HealthCheckResult(context.Registration.FailureStatus, $"{service} is unavailable ({ex.StatusCode})", ex);
        }
    }
}
