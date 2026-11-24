using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// ServiceDefaults платформи SmakoViz. Кожен сервіс викликає ДВА методи:
/// <code>
/// builder.AddServiceDefaults();          // логування, трасування, health checks, service discovery, resilience
/// app.UseMiddleware&lt;CorrelationIdMiddleware&gt;();
/// app.MapDefaultEndpoints();             // /health і /alive
/// </code>
/// Аналогія: це «фірмовий стандарт мережі ресторанів» — кожен заклад (сервіс) готує своє меню,
/// але каса, камери й журнал замовлень (логи, трасування, health) скрізь однакові.
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>Джерело трасування MongoDB-драйвера (пакет MongoDB.Driver.Core.Extensions.DiagnosticSources).</summary>
    public const string MongoActivitySource = "MongoDB.Driver.Core.Extensions.DiagnosticSources";

    /// <summary>Meter метрик кешу (cache.hits / cache.misses з тегом cache.level = L1 | L2) — Лаб. 06.</summary>
    public const string CacheMeterName = "SmakoViz.Cache";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureSerilog();              // 4.1 — Serilog як основний провайдер
        builder.ConfigureOpenTelemetry();        // 4.3 — traces, metrics, logs → OTLP → Aspire Dashboard
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdHandler>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();                 // retry + timeout + circuit breaker (детально — Лаб. 07)
            http.AddServiceDiscovery();                          // "https+http://menu-api" → адреса від Aspire
            http.AddHttpMessageHandler<CorrelationIdHandler>();  // 4.2 — X-Correlation-Id у вихідні запити
        });

        return builder;
    }

    // ------------------------------------------------------------------ 4.1 Serilog
    /// <summary>
    /// Serilog — основний провайдер логування. Кожен запис — JSON-об'єкт з окремими полями
    /// (CompactJsonFormatter), збагачений ServiceName, Environment, MachineName, ThreadId і LogContext
    /// (звідти — CorrelationId). TraceId/SpanId Serilog бере з Activity.Current автоматично (@tr, @sp).
    /// writeToProviders: true — записи йдуть далі в OpenTelemetry → вкладка Structured logs у Dashboard.
    /// </summary>
    public static TBuilder ConfigureSerilog<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName;
        var environmentName = builder.Environment.EnvironmentName;
        var isDevelopment = builder.Environment.IsDevelopment();

        // стандартний консольний провайдер прибираємо, щоб не було дублювання рядків
        builder.Logging.ClearProviders();

        builder.Services.AddSerilog((services, cfg) => cfg
            // рівні за середовищем: у Development більше деталей, у Production — лише важливе
            .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Polly", LogEventLevel.Warning)
            .ReadFrom.Configuration(builder.Configuration)       // секція "Serilog" appsettings може перевизначити рівні
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("Environment", environmentName)
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(new CompactJsonFormatter()),         // JSON у вкладці Console logs
            writeToProviders: true);                              // → OpenTelemetry → Structured logs

        return builder;
    }

    // ------------------------------------------------------------------ 4.3 OpenTelemetry
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // sampling: 100 % у Development, у Production — частка з OpenTelemetry:SamplingRatio (за замовчуванням 25 %)
        var samplingRatio = builder.Environment.IsDevelopment()
            ? 1.0
            : builder.Configuration.GetValue("OpenTelemetry:SamplingRatio", 0.25);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName,
                ["host.name"] = Environment.MachineName
            }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(CacheMeterName)                         // Лаб. 06: cache.hits / cache.misses (L1/L2)
                .AddMeter("SmakoViz.*"))                          // інші власні метрики (Aggregator тощо)
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
                .AddSource(builder.Environment.ApplicationName)   // власні spans сервісу
                .AddSource("SmakoViz.*")                          // бізнес-операції: SmakoViz.Delivery, .Menu, .Reviews, .Aggregator
                .AddSource(MongoActivitySource)                   // MongoDB (Лаб. 04)
                // серверні gRPC-виклики: span ASP.NET Core з атрибутами rpc.service / rpc.method
                .AddAspNetCoreInstrumentation(options =>
                    // health-запити Dashboard не засмічують трасування
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation()                   // вихідні виклики, у т.ч. gRPC-клієнти (HTTP/2)
                .AddSqlClientInstrumentation());                  // Dapper (Лаб. 02) + EF Core (Лаб. 03)

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // адресу OTLP задає Aspire змінною OTEL_EXPORTER_OTLP_ENDPOINT; експорт батчевий — не блокує запити
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    // ------------------------------------------------------------------ Health checks
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);   // «процес живий»

        return builder;
    }

    /// <summary>/health — усі перевірки (у т.ч. БД, якщо сервіс їх додав); /alive — лише «процес живий».</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}

// ====================================================================== 4.2 CorrelationId

/// <summary>
/// Наскрізний ідентифікатор звернення. Береться із заголовка X-Correlation-Id або генерується (GUID),
/// записується назад у Request.Headers (його побачать YARP і CorrelationIdHandler), у LogContext
/// (потрапляє в КОЖЕН лог запиту), у тег поточного span і в заголовок відповіді.
/// Логує межі запиту (boundary logging): «Request started» / «Request finished».
/// Аналогія: номер чека в ресторані — його друкують на касі (Gateway), і кухня, і кур'єр
/// працюють з тим самим номером, а клієнт може назвати його в підтримці.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogProperty = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var generated = false;
        if (!context.Request.Headers.TryGetValue(HeaderName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            value = Guid.NewGuid().ToString();
            context.Request.Headers[HeaderName] = value;   // далі його побачать YARP і HttpClient
            generated = true;
        }

        var correlationId = value.ToString();
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);   // пошук у Traces

        using (LogContext.PushProperty(LogProperty, correlationId))
        using (logger.BeginScope(new Dictionary<string, object> { [LogProperty] = correlationId }))
        {
            if (generated)
                logger.LogInformation("Generated new CorrelationId {CorrelationId}", correlationId);   // аудит

            var sw = Stopwatch.StartNew();
            logger.LogInformation("Request started {Method} {Path}", context.Request.Method, context.Request.Path.Value);
            try
            {
                await next(context);
            }
            finally
            {
                logger.LogInformation("Request finished {Method} {Path} with {StatusCode} in {ElapsedMs} ms",
                    context.Request.Method, context.Request.Path.Value, context.Response.StatusCode, sw.ElapsedMilliseconds);
            }
        }
    }
}

/// <summary>DelegatingHandler: той самий X-Correlation-Id — у кожен вихідний HTTP-запит до інших сервісів.</summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = accessor.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        if (!string.IsNullOrEmpty(id) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);

        return base.SendAsync(request, ct);
    }
}
