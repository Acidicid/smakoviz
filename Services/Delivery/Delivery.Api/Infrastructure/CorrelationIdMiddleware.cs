using Serilog.Context;

namespace Delivery.Api.Infrastructure;

/// <summary>
/// Кореляційний ідентифікатор запиту: береться із заголовка X-Correlation-Id
/// (або генерується) і додається до кожного рядка логу та до відповіді.
/// Так усі записи одного запиту легко знайти разом. Секрети в лог не пишуться.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var value)
                            && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : context.TraceIdentifier;

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
