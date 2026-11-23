using Menu.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Menu.Api.Infrastructure;

/// <summary>
/// Глобальна обробка ВСІХ невиловлених винятків → ProblemDetails з правильним статусом:
///   EntityNotFoundException → 404
///   ValidationException     → 400 (ValidationProblemDetails з помилками по полях)
///   ConflictException       → 409 (дубль slug/назви, RowVersion, ресторан зі стравами)
///   DomainException         → 409 (порушене правило сутності: ціна ≤ 0, замало порцій)
///   усе інше                → 500
/// Деталі внутрішніх помилок (повідомлення винятку) — ЛИШЕ в Development.
/// Виняток логується ОДИН раз — тут (вбудований лог ExceptionHandlerMiddleware вимкнено в appsettings).
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            EntityNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            DomainException => (StatusCodes.Status409Conflict, "Business rule violated"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            logger.LogError(exception, "Request {Method} {RequestPath} failed with {StatusCode}",
                http.Request.Method, http.Request.Path.Value, status);
        else
            logger.LogWarning("Request {Method} {RequestPath} failed with {StatusCode}: {Reason}",
                http.Request.Method, http.Request.Path.Value, status, exception.Message);

        ProblemDetails problem = exception is ValidationException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Type = $"https://httpstatuses.io/{status}";
        problem.Detail = status < 500
            ? exception.Message                       // безпечні повідомлення доменних винятків
            : environment.IsDevelopment()
                ? exception.Message                   // внутрішні деталі — лише в Development
                : "An unexpected error occurred. Use traceId when contacting support.";

        if (exception is EntityNotFoundException notFound)
        {
            problem.Extensions["entity"] = notFound.Entity;
            problem.Extensions["key"] = notFound.Key.ToString();
        }

        http.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
