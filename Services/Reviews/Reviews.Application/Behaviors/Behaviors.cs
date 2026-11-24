using System.Diagnostics;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Behaviors;

// Pipeline Behaviors — «обгортки» навколо КОЖНОЇ команди й запиту MediatR (як middleware в ASP.NET Core).
// Порядок задано в DependencyInjection.AddApplication:
//   LoggingBehavior → ValidationBehavior → PerformanceBehavior → обробник

/// <summary>
/// 1 (зовнішній). Структуроване логування: що прийшло, чим закінчилось, скільки тривало.
/// Якщо валідація відхилила запит, у лозі буде «rejected by validation» — обробник не виконувався.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName} {@Request}", requestName, request);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            logger.LogInformation("Handled {RequestName} in {ElapsedMs} ms", requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (ValidationException ex)
        {
            logger.LogWarning("{RequestName} rejected by validation ({ErrorCount} errors) — handler was NOT executed",
                requestName, ex.Errors.Count());
            throw;
        }
        catch (Exception ex)
        {
            // сам виняток (зі стеком) логує глобальний обробник у WebApi — тут лише факт і тип
            logger.LogWarning("{RequestName} failed after {ElapsedMs} ms: {ExceptionType}",
                requestName, stopwatch.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}

/// <summary>
/// 2. Валідація: виконує ВСІ валідатори FluentValidation для запиту (синхронні й асинхронні,
/// у т.ч. з запитом до MongoDB), збирає всі помилки разом і кидає ValidationException
/// ДО виклику обробника.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);   // обробник не викликається

        return await next();
    }
}

/// <summary>
/// 3 (найближчий до обробника). Вимірює час саме обробника (запити до MongoDB)
/// і попереджає в лозі про аномально повільні операції.
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    /// <summary>Поріг «повільного» обробника, мс.</summary>
    public const int SlowThresholdMs = 500;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > SlowThresholdMs)
            logger.LogWarning("Slow handler {RequestName}: {ElapsedMs} ms (threshold {ThresholdMs} ms) {@Request}",
                typeof(TRequest).Name, stopwatch.ElapsedMilliseconds, SlowThresholdMs, request);

        return response;
    }
}
