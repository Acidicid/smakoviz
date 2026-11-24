using Reviews.Application;
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

// ---------- Health check MongoDB (входить у /health зі ServiceDefaults) ----------
builder.Services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb");

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
app.MapDefaultEndpoints();   // /health (у т.ч. mongodb) і /alive
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Для інтеграційних тестів (WebApplicationFactory).</summary>
public partial class Program;
