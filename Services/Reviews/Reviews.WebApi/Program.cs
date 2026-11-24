using Reviews.Application;
using Reviews.Application.Caching;
using Reviews.Grpc;
using Reviews.WebApi.IntegrationEvents;
using Platform.EventBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reviews.Application.Common.Mappings;
using Reviews.Infrastructure;
using Reviews.WebApi.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Лаб. 05: ServiceDefaults — Serilog, OpenTelemetry, health checks, service discovery ----------
builder.AddServiceDefaults();

// ---------- Шари Clean Architecture: кожен реєструє себе власним методом розширення ----------
builder.Services
    .AddApplication(builder.Configuration)      // MediatR (обробники + behaviors), валідатори FluentValidation
    .AddInfrastructure(builder.Configuration);  // MongoDB: IOptions, BsonClassMap, репозиторії, індекси, сідінг

// AutoMapper: профілі з Application (ObjectId → string, Value Objects, вкладені масиви)
builder.Services.AddAutoMapper(cfg =>
{
    cfg.LicenseKey = builder.Configuration["LuckyPenny:LicenseKey"];
    cfg.AddMaps(typeof(ReviewProfile).Assembly);
});

builder.Services.AddControllers();

// ---------- Помилки → ProblemDetails (включно з MongoDB-винятками) ----------
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ---------- Лаб. 06: кешування L1 (IMemoryCache) + L2 (Redis) — ITwoLevelCache реєструє AddApplication ----------
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 10_000;          // кожен запис Size = 1
    options.CompactionPercentage = 0.25;
    options.TrackStatistics = true;
});
var redisEnabled = !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis"));
if (redisEnabled)
    builder.AddRedisDistributedCache("redis", settings => settings.DisableHealthChecks = true);   // свій check — Degraded
else
    builder.Services.AddDistributedMemoryCache();

// ---------- Лаб. 06: gRPC + серверний interceptor логування ----------
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcLoggingInterceptor>();
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// ---------- Health check MongoDB (входить у /health зі ServiceDefaults) ----------
// Лаб. 07: імена, теги ("ready" → /health/ready) і таймаути
var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck<MongoHealthCheck>("reviews-db", HealthStatus.Unhealthy, ["database", "mongodb", "ready"], TimeSpan.FromSeconds(5))
    .AddCheck<CacheHealthCheck>("cache", HealthStatus.Degraded, ["cache"]);
if (redisEnabled)
    healthChecks.AddCheck<RedisHealthCheck>("redis-cache", HealthStatus.Degraded, ["cache", "redis", "ready"], TimeSpan.FromSeconds(3));
builder.Services.AddGrpcHealthChecks();   // gRPC Health Checking Protocol

// ---------- Лаб. 08: RabbitMQ — споживач подій «Меню» (денормалізована назва страви у відгуках) ----------
builder.AddRabbitMQClient("messaging");
builder.Services.AddEventConsumer(ReviewsRoutingKeys.Queue, consumer => consumer
    .Subscribe<DishUpdatedIntegrationEvent, DishUpdatedIntegrationEventHandler>(ReviewsRoutingKeys.DishUpdated));

// ---------- Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xml = Path.Combine(AppContext.BaseDirectory, "Reviews.WebApi.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
    // Command-и в різних папках можуть мати однакові короткі імена — повне ім'я типу як schemaId
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // зі ServiceDefaults
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<ReviewsGrpcService>();   // Лаб. 06: Protos/reviews.proto
app.MapGrpcHealthChecksService();            // Лаб. 07: grpc.health.v1.Health/Check
app.MapDefaultEndpoints();   // /health (у т.ч. mongodb) і /alive
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Для інтеграційних тестів (WebApplicationFactory).</summary>
public partial class Program;
