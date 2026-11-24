using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Platform.Aggregator.Resilience;

/// <summary>
/// Профіль відмовостійкості клієнта. Значення ОБҐРУНТОВАНІ критичністю сервісу (таблиця — README, розділ 14):
///
/// КРИТИЧНІ — delivery-api, menu-api: без замовлення картку не зібрати (503), без меню — картки ресторану.
///   Більше повторів (4) з експоненційною затримкою + jitter, ЧУТЛИВІШИЙ circuit breaker (30 % збоїв),
///   довший загальний таймаут — краще трохи почекати, ніж віддати помилку.
/// НЕКРИТИЧНІ — reviews-api: Aggregator і без відгуків поверне часткову відповідь (Warnings).
///   1 повтор, короткі таймаути, ТОЛЕРАНТНІШИЙ breaker (70 %) — швидко здатися, а не тримати клієнта.
///
/// Узгодженість таймаутів: Total ≥ (1 + MaxRetryAttempts) × AttemptTimeout + сума затримок;
/// SamplingDuration ≥ 2 × AttemptTimeout (інакше валідація опцій не пропустить застосунок).
/// </summary>
public sealed record ResilienceProfile(
    string Name,
    int MaxRetryAttempts,
    TimeSpan RetryDelay,
    TimeSpan AttemptTimeout,
    TimeSpan TotalTimeout,
    double FailureRatio,
    int MinimumThroughput,
    TimeSpan SamplingDuration,
    TimeSpan BreakDuration,
    int GrpcMaxAttempts)
{
    //                                             retry  delay                       attempt                    total                       ratio  min  sampling                    break                      grpc
    public static readonly ResilienceProfile Critical =
        new("critical", 4, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(20), 0.3, 5, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15), 3);

    public static readonly ResilienceProfile NonCritical =
        new("non-critical", 1, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), 0.7, 5, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), 2);
}

public static class ResilienceExtensions
{
    /// <summary>
    /// HTTP-клієнт: прибираємо стандартний обробник з ServiceDefaults (щоб НЕ нашаровувати) і додаємо свій
    /// Standard Resilience Handler з параметрами профілю + логуванням переходів circuit breaker.
    /// </summary>
    public static IHttpClientBuilder AddProfiledResilience(this IHttpClientBuilder builder, string service, ResilienceProfile profile)
    {
        builder.RemoveAllResilienceHandlers();
        builder.AddStandardResilienceHandler().Configure((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Platform.Aggregator.Resilience");

            options.Retry.MaxRetryAttempts = profile.MaxRetryAttempts;
            options.Retry.Delay = profile.RetryDelay;
            options.Retry.BackoffType = DelayBackoffType.Exponential;   // 0.3 → 0.6 → 1.2 → 2.4 с
            options.Retry.UseJitter = true;                              // розкид, щоб клієнти не били синхронно
            options.Retry.DisableForUnsafeHttpMethods();                 // POST/PUT/DELETE — без повторів
            options.Retry.OnRetry = args =>
            {
                logger.LogWarning("Retry {Attempt} to {Service} in {Delay} ms: {Reason}", args.AttemptNumber + 1, service,
                    args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                return ValueTask.CompletedTask;
            };

            options.AttemptTimeout.Timeout = profile.AttemptTimeout;
            options.TotalRequestTimeout.Timeout = profile.TotalTimeout;

            options.CircuitBreaker.FailureRatio = profile.FailureRatio;
            options.CircuitBreaker.MinimumThroughput = profile.MinimumThroughput;
            options.CircuitBreaker.SamplingDuration = profile.SamplingDuration;
            options.CircuitBreaker.BreakDuration = profile.BreakDuration;
            options.CircuitBreaker.OnOpened = args =>
            {
                logger.LogError("Circuit to {Service} OPENED for {BreakDuration} ({Profile} profile): {Reason}",
                    service, args.BreakDuration, profile.Name, args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                return ValueTask.CompletedTask;
            };
            options.CircuitBreaker.OnHalfOpened = _ =>
            {
                logger.LogWarning("Circuit to {Service} HALF-OPEN: probing with a trial request", service);
                return ValueTask.CompletedTask;
            };
            options.CircuitBreaker.OnClosed = _ =>
            {
                logger.LogInformation("Circuit to {Service} CLOSED: service recovered", service);
                return ValueTask.CompletedTask;
            };
        });
        return builder;
    }

    /// <summary>
    /// gRPC-клієнт. gRPC повертає Unavailable / DeadlineExceeded у трейлерах з HTTP 200 — HTTP-обробник їх НЕ бачить.
    /// Тому: (1) retry — вбудованою політикою gRPC-каналу за gRPC-статусами (ServiceConfig → MethodConfig → RetryPolicy);
    /// (2) HTTP-рівень лишає лише таймаут спроби й circuit breaker (мережеві збої) — БЕЗ HTTP-retry, щоб повтори
    /// не множилися (gRPC × HTTP). Усі методи gRPC-контрактів — читання, отже ідемпотентні й безпечні для повтору.
    /// </summary>
    public static IHttpClientBuilder AddProfiledGrpcResilience(this IHttpClientBuilder builder, string service, ResilienceProfile profile)
    {
        builder.ConfigureChannel(channel => channel.ServiceConfig = new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },   // усі методи сервісу
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = profile.GrpcMaxAttempts,          // разом з першою спробою
                        InitialBackoff = profile.RetryDelay,
                        MaxBackoff = TimeSpan.FromSeconds(2),
                        BackoffMultiplier = 2,                           // експоненційно; gRPC сам додає jitter
                        RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.ResourceExhausted }
                    }
                }
            }
        });

        builder.RemoveAllResilienceHandlers();
        builder.AddResilienceHandler($"grpc-{service}", (pipeline, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Platform.Aggregator.Resilience");
            pipeline
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = profile.FailureRatio,
                    MinimumThroughput = profile.MinimumThroughput,
                    SamplingDuration = profile.SamplingDuration,
                    BreakDuration = profile.BreakDuration,
                    OnOpened = args =>
                    {
                        logger.LogError("gRPC circuit to {Service} OPENED for {BreakDuration} ({Profile} profile)",
                            service, args.BreakDuration, profile.Name);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogInformation("gRPC circuit to {Service} CLOSED", service);
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(profile.AttemptTimeout);   // одна спроба (gRPC retry повторить у межах deadline виклику)
        });
        return builder;
    }
}
