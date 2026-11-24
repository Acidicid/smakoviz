using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using DeliveryGrpc = Delivery.Grpc;
using MenuGrpc = Menu.Grpc;
using ReviewsGrpc = Reviews.Grpc;

// =====================================================================
// SmakoViz · Aggregator — композиція даних кількох мікросервісів в одну відповідь.
// Для зовнішніх клієнтів — HTTP-ендпоінт (через Gateway: http://localhost:5000/api/aggregator/...),
// до мікросервісів — gRPC (Лаб. 06). HTTP-клієнти Лаб. 05 лишено для порівняння затримок (?transport=http).
// =====================================================================
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, service discovery, resilience, CorrelationIdHandler

// ---------- Лаб. 06: типізовані gRPC-клієнти (згенеровані з .proto) з адресою за логічним іменем ----------
// Створюються через ту саму фабрику HttpClient, тож ServiceDefaults діє і тут: service discovery
// ("https://menu-api" → реальна адреса), resilience і CorrelationIdHandler (X-Correlation-Id → метадані gRPC).
builder.Services.AddGrpcClient<DeliveryGrpc.DeliveryService.DeliveryServiceClient>(o => o.Address = new Uri("https://delivery-api"));
builder.Services.AddGrpcClient<MenuGrpc.MenuService.MenuServiceClient>(o => o.Address = new Uri("https://menu-api"));
builder.Services.AddGrpcClient<ReviewsGrpc.ReviewsService.ReviewsServiceClient>(o => o.Address = new Uri("https://reviews-api"));

// ---------- Лаб. 05: typed HttpClients (для порівняння HTTP vs gRPC) ----------
builder.Services.AddHttpClient<DeliveryClient>(c => c.BaseAddress = new("https+http://delivery-api"));
builder.Services.AddHttpClient<MenuClient>(c => c.BaseAddress = new("https+http://menu-api"));
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new("https+http://reviews-api"));

builder.Services.AddScoped<GrpcPlatformClient>();
builder.Services.AddScoped<HttpPlatformClient>();
builder.Services.AddScoped<AggregationService>();
builder.Services.AddSingleton<AggregatorMetrics>();

// ---------- Лаб. 06: Redis — кеш агрегованих відповідей (TTL 30–60 с) ----------
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis")))
    builder.AddRedisDistributedCache("redis");
else
    builder.Services.AddDistributedMemoryCache();

// ---------- JSON: camelCase, null-значення не серіалізуються ----------
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // CorrelationId від Gateway → LogContext → вихідні gRPC/HTTP-виклики
app.UseExceptionHandler();
app.UseStatusCodePages();

var api = app.MapGroup("/api/aggregator");

// повна картка замовлення: замовлення + актуальне меню + рейтинг + відгуки клієнта
// ?transport=http — ті самі дані через REST (Лаб. 05) для порівняння; ?noCache=true — без кешу Redis
api.MapGet("/orders/{id:long}/full", async (long id, HttpContext http, AggregationService aggregator,
    string? transport, bool? noCache, CancellationToken ct) =>
    await Respond(http, () => aggregator.GetFullOrderAsync(id, ParseTransport(transport), noCache != true, ct),
        $"Order {id} not found", ParseTransport(transport)))
   .WithName("GetFullOrder");

// картка ресторану: меню + рейтинг + 3 останні відгуки
api.MapGet("/restaurants/{id:long}", async (long id, HttpContext http, AggregationService aggregator,
    string? transport, bool? noCache, CancellationToken ct) =>
    await Respond(http, () => aggregator.GetRestaurantOverviewAsync(id, ParseTransport(transport), noCache != true, ct),
        $"Restaurant {id} not found", ParseTransport(transport)))
   .WithName("GetRestaurantOverview");

app.MapDefaultEndpoints();

app.Run();

static Transport ParseTransport(string? value)
    => string.Equals(value, "http", StringComparison.OrdinalIgnoreCase) ? Transport.Http : Transport.Grpc;

static async Task<IResult> Respond<T>(HttpContext http, Func<Task<AggregationResult<T>?>> load, string notFound, Transport transport)
{
    try
    {
        var result = await load();
        if (result is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: notFound);

        http.Response.Headers["X-Cache"] = result.FromCache ? "HIT" : "MISS";
        http.Response.Headers["X-Aggregator-Transport"] = transport.ToString().ToLowerInvariant();
        http.Response.Headers["X-Aggregator-Elapsed-Ms"] = result.ElapsedMs.ToString();
        return Results.Ok(result.Value);
    }
    catch (DependencyUnavailableException ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Dependency unavailable", detail: ex.Message);
    }
}
