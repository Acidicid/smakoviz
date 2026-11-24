using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Menu.Api.HealthChecks;

/// <summary>
/// SQL Server: найлегший запит SELECT 1 (виконується регулярно — важкі запити неприпустимі).
/// Ім'я "menu-db", теги database / sql / ready, таймаут 5 с — задаються при реєстрації в Program.cs.
/// </summary>
public sealed class SqlServerHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy($"SQL Server '{connection.Database}' is reachable");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "SQL Server is unreachable", ex);
        }
    }
}

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
                "Redis is unavailable — cache disabled, reading from database", ex);
        }
    }
}
