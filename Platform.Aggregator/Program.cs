using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;

// =====================================================================
// SmakoViz · Aggregator — композиція даних кількох мікросервісів в одну відповідь.
// Клієнт ходить сюди через Gateway: http://localhost:5000/api/aggregator/...
// =====================================================================
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, service discovery, resilience, CorrelationIdHandler

// ---------- Typed HttpClients: логічні імена ресурсів Aspire ("https+http" = HTTPS, якщо є, інакше HTTP) ----------
builder.Services.AddHttpClient<DeliveryClient>(c => c.BaseAddress = new("https+http://delivery-api"));
builder.Services.AddHttpClient<MenuClient>(c => c.BaseAddress = new("https+http://menu-api"));
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new("https+http://reviews-api"));

builder.Services.AddScoped<AggregationService>();

// ---------- JSON: camelCase, null-значення не серіалізуються ----------
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // CorrelationId від Gateway → LogContext → вихідні запити
app.UseExceptionHandler();
app.UseStatusCodePages();

var api = app.MapGroup("/api/aggregator");

// повна картка замовлення: замовлення + актуальне меню + рейтинг + відгуки клієнта
api.MapGet("/orders/{id:long}/full", async (long id, AggregationService aggregator, CancellationToken ct) =>
{
    try
    {
        var result = await aggregator.GetFullOrderAsync(id, ct);
        return result is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found", detail: $"Order {id} not found")
            : Results.Ok(result);
    }
    catch (DependencyUnavailableException ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Dependency unavailable", detail: ex.Message);
    }
}).WithName("GetFullOrder");

// картка ресторану: меню + рейтинг + 3 останні відгуки
api.MapGet("/restaurants/{id:long}", async (long id, AggregationService aggregator, CancellationToken ct) =>
{
    try
    {
        var result = await aggregator.GetRestaurantOverviewAsync(id, ct);
        return result is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Restaurant not found", detail: $"Restaurant {id} not found")
            : Results.Ok(result);
    }
    catch (DependencyUnavailableException ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Dependency unavailable", detail: ex.Message);
    }
}).WithName("GetRestaurantOverview");

app.MapDefaultEndpoints();

app.Run();
