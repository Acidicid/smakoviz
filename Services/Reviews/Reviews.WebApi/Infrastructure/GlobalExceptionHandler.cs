using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Reviews.Application.Common.Exceptions;
using Reviews.Domain.Exceptions;

namespace Reviews.WebApi.Infrastructure;

/// <summary>
/// Глобальна обробка ВСІХ невиловлених винятків → ProblemDetails, у т.ч. MongoDB-специфічних:
///
///   NotFoundException                         → 404
///   ValidationException (FluentValidation)    → 400 (ValidationProblemDetails: помилки згруповані по полях)
///   DomainException                           → 400 (порушене правило сутності / Value Object)
///   ConflictException (конфлікт версій)       → 409
///   MongoWriteException / MongoBulkWriteException з DuplicateKey → 409 (unique-індекс)
///   інші помилки запису MongoDB               → 400
///   MongoConnectionException, TimeoutException (сервер MongoDB недоступний) → 503
///   усе інше                                  → 500 (деталі — лише в Development)
/// Логується ОДИН раз — тут.
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
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            DomainException => (StatusCodes.Status400BadRequest, "Business rule violated"),
            ConflictException => (StatusCodes.Status409Conflict, "Concurrency conflict"),
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey }
                => (StatusCodes.Status409Conflict, "Duplicate key"),
            MongoBulkWriteException bulk when bulk.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey)
                => (StatusCodes.Status409Conflict, "Duplicate key"),
            MongoWriteException => (StatusCodes.Status400BadRequest, "Database write rejected"),
            MongoConnectionException or TimeoutException
                => (StatusCodes.Status503ServiceUnavailable, "Database is unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            logger.LogError(exception, "{Method} {Path} failed with {StatusCode}", http.Request.Method, http.Request.Path.Value, status);
        else
            logger.LogWarning("{Method} {Path} failed with {StatusCode}: {Reason}",
                http.Request.Method, http.Request.Path.Value, status, exception.Message);

        ProblemDetails problem = exception is ValidationException validation
            ? new ValidationProblemDetails(validation.Errors
                .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "request" : e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Type = $"https://httpstatuses.io/{status}";
        problem.Detail = exception switch
        {
            ValidationException => "One or more validation errors occurred",
            NotFoundException or DomainException or ConflictException => exception.Message,
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } or MongoBulkWriteException
                => "A document with the same unique key already exists (e.g. you have already reviewed this target)",
            _ when environment.IsDevelopment() => exception.Message,   // внутрішні деталі — лише в Development
            _ => null
        };

        http.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
