using FluentValidation;
using Menu.Api.HealthChecks;
using Menu.Api.Infrastructure;
using Menu.Api.Metrics;
using Menu.Bll.Caching;
using Menu.Bll.Mapping;
using Menu.Bll.Services;
using Menu.Bll.Validation;
using Menu.Dal.Context;
using Menu.Dal.Repositories;
using Menu.Dal.Seeding;
using Menu.Grpc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Лаб. 05: ServiceDefaults — Serilog, OpenTelemetry, health checks, service discovery ----------
builder.AddServiceDefaults();

// ---------- EF Core ----------
// Рядок підключення: appsettings.json → ConnectionStrings:MenuDb (Aspire передає ConnectionStrings__MenuDb)
var connectionString = builder.Configuration.GetConnectionString("MenuDb")
    ?? throw new InvalidOperationException("Connection string 'MenuDb' is missing");

builder.Services.AddDbContext<MenuDbContext>(options => options
    .UseSqlServer(connectionString)
    // фільтр м'якого видалення на Restaurant/Dish і обов'язкові навігації — свідоме рішення, не помилка
    .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

// ---------- DI: Api → Bll → Dal; усе Scoped (один DbContext і один UoW на HTTP-запит) ----------
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
// Лаб. 06: сервіси з Лаб. 03 загорнуті в кешовані декоратори (Menu.Bll/Caching) — і контролери, і gRPC
// отримують IDishService/IRestaurantService/ICategoryService уже з кешем L1 → L2 → БД.
builder.Services.AddScoped<RestaurantService>();
builder.Services.AddScoped<DishService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<IRestaurantService, CachedRestaurantService>();
builder.Services.AddScoped<CachedDishService>();
// Лаб. 07: зовнішній шар — метрики (InstrumentedDishService → CachedDishService → DishService)
builder.Services.AddScoped<IDishService, InstrumentedDishService>();
builder.Services.AddSingleton<MenuMetrics>();   // Meter "Menu.Api"
builder.Services.AddScoped<ICategoryService, CachedCategoryService>();

// ---------- Лаб. 06: кешування ----------
// L1 — IMemoryCache з обмеженням розміру: кожен запис має Size = 1, тож не більше 10 000 записів;
// при переповненні витісняється 25 % (спершу Low-пріоритет і найдавніше використані).
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 10_000;
    options.CompactionPercentage = 0.25;
    options.TrackStatistics = true;   // статистика для /health
});
// L2 — IDistributedCache поверх Redis через Aspire-інтеграцію (ConnectionStrings:redis передає AppHost;
// інтеграція сама додає retry, health check і трасування Redis). Без Aspire — кеш у пам'яті, щоб сервіс запускався.
var redisEnabled = !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis"));
if (redisEnabled)
    // власний health check Redis нижче (Degraded) замість вбудованого в інтеграцію (Unhealthy)
    builder.AddRedisDistributedCache("redis", settings => settings.DisableHealthChecks = true);
else
    builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<CacheMetrics>();
builder.Services.AddSingleton<ITwoLevelCache, TwoLevelCache>();
builder.Services.AddHostedService<MenuCacheWarmupService>();   // прогрівання категорій і карток ресторанів

// ---------- Лаб. 07: health checks (теги: "ready" → /health/ready, "live" → /health/live) ----------
var healthChecks = builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<SqlServerHealthCheck>("menu-db", HealthStatus.Unhealthy,
        ["database", "sql", "ready"], TimeSpan.FromSeconds(5), connectionString)          // SELECT 1
    .AddCheck<CacheHealthCheck>("cache", HealthStatus.Degraded, ["cache"]);
if (redisEnabled)
    healthChecks.AddCheck<RedisHealthCheck>("redis-cache", HealthStatus.Degraded,       // кеш необов'язковий
        ["cache", "redis", "ready"], TimeSpan.FromSeconds(3));
builder.Services.AddGrpcHealthChecks();   // gRPC Health Checking Protocol

// ---------- Лаб. 06: gRPC (HTTP/2 через HTTPS-endpoint, ALPN) + серверний interceptor логування ----------
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcLoggingInterceptor>();
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddAutoMapper(cfg =>
{
    cfg.LicenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    cfg.AddProfile<MenuProfile>();
});

// FluentValidation: валідатори з окремих класів Menu.Bll/Validation; викликаються ЯВНО в сервісах
builder.Services.AddValidatorsFromAssemblyContaining<CreateDishDtoValidator>();

builder.Services.AddControllers();

// ---------- Помилки → ProblemDetails (RFC 9457) ----------
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ---------- Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xml = Path.Combine(AppContext.BaseDirectory, "Menu.Api.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

var app = builder.Build();

// ---------- Міграції + сідінг (ідемпотентно) ----------
// 1) MigrateAsync застосовує міграції з Menu.Dal/Migrations (у т.ч. HasData довідника категорій);
// 2) DbInitializer додає ресторани й страви, лише якщо їх ще немає. У Production — вимкнено.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
    await db.Database.MigrateAsync();

    if (!app.Environment.IsProduction() && app.Configuration.GetValue("Database:Seed", true))
        await DbInitializer.SeedAsync(db, app.Logger);
}

app.UseMiddleware<CorrelationIdMiddleware>();   // зі ServiceDefaults
app.UseSerilogRequestLogging(options =>
{
    // додаткові структуровані властивості в рядку «HTTP GET /api/menu/dishes responded 200 in 12 ms»
    options.EnrichDiagnosticContext = (diagnostics, http) =>
    {
        diagnostics.Set("RequestPath", http.Request.Path.Value ?? "");
        diagnostics.Set("UserId", http.User.Identity?.Name ?? "anonymous");
    };
});
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<MenuGrpcService>();   // Лаб. 06: Protos/menu.proto
app.MapGrpcHealthChecksService();         // Лаб. 07: grpc.health.v1.Health/Check (для Aggregator)
app.MapDefaultEndpoints();   // /health і /alive
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Для інтеграційних тестів (WebApplicationFactory).</summary>
public partial class Program;
