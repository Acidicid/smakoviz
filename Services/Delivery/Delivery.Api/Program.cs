using Delivery.Api.HealthChecks;
using Delivery.Api.IntegrationEvents;
using Delivery.Api.Infrastructure;
using Delivery.Api.Metrics;
using Delivery.Bll.Caching;
using Delivery.Bll.Mapping;
using Delivery.Bll.Services;
using Delivery.Dal.Abstractions;
using Delivery.Dal.Infrastructure;
using Delivery.Dal.Repositories;
using Delivery.Grpc;
using Platform.EventBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// ---------- Лаб. 05: ServiceDefaults — Serilog, OpenTelemetry, health checks, service discovery ----------
builder.AddServiceDefaults();

// ---------- Рядок підключення ----------
// appsettings.json → ConnectionStrings:DeliveryDb; перевизначається змінною оточення
// ConnectionStrings__DeliveryDb (так його й передає .NET Aspire).
var connectionString = builder.Configuration.GetConnectionString("DeliveryDb")
    ?? throw new InvalidOperationException("Connection string 'DeliveryDb' is missing");

// ---------- DI (composition root): Api → Bll → Dal ----------
// Unit of Work — Scoped: один екземпляр і ОДНЕ з'єднання на HTTP-запит.
// Репозиторії окремо не реєструються: вони доступні лише через UoW (спільна транзакція).
builder.Services.AddScoped<IUnitOfWork>(_ => new UnitOfWork(connectionString));

// Сервіси BLL — теж Scoped, бо залежать від Scoped Unit of Work.
builder.Services.AddScoped<ICustomerService, CustomerService>();
// Лаб. 06: читання страв і замовлень — через кешовані декоратори (Delivery.Bll/Caching)
builder.Services.AddScoped<DishService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<IDishService, CachedDishService>();
builder.Services.AddScoped<CachedOrderService>();
builder.Services.AddScoped<PublishingOrderService>();   // Лаб. 08: OrderPlaced після COMMIT
// Лаб. 07: зовнішній шар — метрики (InstrumentedOrderService → CachedOrderService → OrderService)
builder.Services.AddScoped<IOrderService, InstrumentedOrderService>();

// ---------- Лаб. 07: бізнес-метрики (Meter "Delivery.Api", Singleton) ----------
builder.Services.AddSingleton<OrdersSnapshot>();
builder.Services.AddSingleton<DeliveryMetrics>();
builder.Services.AddHostedService<OrdersSnapshotRefresher>();   // оновлює gauge orders.active

// ---------- Лаб. 06: кешування L1 (пам'ять, обмежений розмір) + L2 (Redis через Aspire) ----------
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 10_000;          // кожен запис Size = 1
    options.CompactionPercentage = 0.25; // при переповненні витісняється 25 % (спершу Low-пріоритет)
    options.TrackStatistics = true;
});
var redisEnabled = !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis"));
if (redisEnabled)
    // власний health check Redis нижче (Degraded) замість вбудованого в інтеграцію (Unhealthy)
    builder.AddRedisDistributedCache("redis", settings => settings.DisableHealthChecks = true);
else
    builder.Services.AddDistributedMemoryCache();  // запуск без Aspire
builder.Services.AddSingleton<CacheMetrics>();
builder.Services.AddSingleton<ITwoLevelCache, TwoLevelCache>();

// ---------- Лаб. 07: health checks (теги: "ready" → /health/ready, "live" → /health/live) ----------
var healthChecks = builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<SqlServerHealthCheck>("delivery-db", HealthStatus.Unhealthy,
        ["database", "sql", "ready"], TimeSpan.FromSeconds(5), connectionString)          // SELECT 1
    .AddCheck<CacheHealthCheck>("cache", HealthStatus.Degraded, ["cache"]);
if (redisEnabled)
    healthChecks.AddCheck<RedisHealthCheck>("redis-cache", HealthStatus.Degraded,       // кеш необов'язковий
        ["cache", "redis", "ready"], TimeSpan.FromSeconds(3));
builder.Services.AddGrpcHealthChecks();   // gRPC Health Checking Protocol на основі тих самих перевірок

// ---------- Лаб. 08: RabbitMQ ----------
// Aspire-інтеграція: IConnection (RabbitMQ.Client 7, асинхронний IChannel) + health check + трасування.
builder.AddRabbitMQClient("messaging");
builder.Services.AddEventPublisher();   // delivery.order.placed
// Споживач: черга delivery-api.events — реплікація локального довідника страв з «Меню»
builder.Services.AddEventConsumer(DeliveryRoutingKeys.Queue, consumer => consumer
    .Subscribe<DishUpdatedIntegrationEvent, DishUpdatedIntegrationEventHandler>(DeliveryRoutingKeys.DishUpdated)
    .Subscribe<DishDeletedIntegrationEvent, DishDeletedIntegrationEventHandler>(DeliveryRoutingKeys.DishDeleted));

// ---------- Лаб. 06: gRPC + серверний interceptor логування (зі ServiceDefaults) ----------
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcLoggingInterceptor>();
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// AutoMapper: профілі зібрані централізовано в Delivery.Bll/Mapping
builder.Services.AddAutoMapper(cfg =>
{
    cfg.LicenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    cfg.AddProfile<DeliveryProfile>();
});

builder.Services.AddControllers();

// ---------- Помилки: доменні винятки → HTTP-статус у форматі ProblemDetails (RFC 9457) ----------
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// ---------- OpenAPI / Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xml = Path.Combine(AppContext.BaseDirectory, "Delivery.Api.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

var app = builder.Build();

// ---------- Розгортання БД скриптами Лаб. 01 (для порожнього контейнера Aspire) ----------
if (app.Configuration.GetValue<bool>("Database:Initialize"))
    await DatabaseInitializer.InitializeAsync(connectionString, app.Logger);

// Лаб. 08: таблиця ідемпотентності споживача (IF OBJECT_ID … IS NULL CREATE TABLE)
await ProcessedEventRepository.EnsureCreatedAsync(connectionString);

app.UseMiddleware<CorrelationIdMiddleware>();   // зі ServiceDefaults
app.UseExceptionHandler();
app.UseStatusCodePages();   // 404 на неіснуючий маршрут теж у форматі ProblemDetails

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<DeliveryGrpcService>();   // Лаб. 06: Protos/delivery.proto
app.MapGrpcHealthChecksService();             // Лаб. 07: grpc.health.v1.Health/Check (для Aggregator)
app.MapDefaultEndpoints();   // /health і /alive
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Потрібен для інтеграційних тестів через WebApplicationFactory.</summary>
public partial class Program;
