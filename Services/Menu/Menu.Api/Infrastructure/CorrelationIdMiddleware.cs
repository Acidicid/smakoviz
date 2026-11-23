using Serilog.Context;

namespace Menu.Api.Infrastructure;

/// <summary>
/// CorrelationId із заголовка X-Correlation-Id (або TraceIdentifier) додається до КОЖНОГО рядка логу
/// цього запиту (у т.ч. SQL-команд EF Core) і повертається у відповіді.
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
