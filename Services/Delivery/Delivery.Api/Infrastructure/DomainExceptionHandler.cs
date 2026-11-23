using Delivery.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Delivery.Api.Infrastructure;

/// <summary>
/// ЄДИНЕ місце, де винятки перетворюються на HTTP-статуси (а не try/catch у кожній дії).
/// Завершальна ланка ланцюжка:
///   THROW 50002 у процедурі → SqlException → BusinessConflictException (DAL) → 409 ProblemDetails (тут)
///
///   NotFoundException            → 404
///   ConcurrencyConflictException → 409 (RowVersion змінився)
///   BusinessConflictException    → 409 (недопустимий перехід статусу, дубль email)
///   BusinessRuleException        → 422 (страва недоступна, порожнє замовлення)
///   ValidationException          → 400 (+ перелік полів з помилками)
///   усе інше                     → 500 без внутрішніх деталей
/// </summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "Concurrency conflict"),
            BusinessRuleException => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            BusinessConflictException => (StatusCodes.Status409Conflict, "Business conflict"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception");
        else
            logger.LogWarning("Request failed with {Status}: {Message}", status, exception.Message);

        ProblemDetails problem = exception is ValidationException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        // повідомлення доменних винятків безпечні для клієнта; для 500 — нічого не розкриваємо
        problem.Detail = exception is DomainException ? exception.Message : null;
        problem.Type = $"https://httpstatuses.io/{status}";

        http.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
