using Delivery.Api.Infrastructure;
using Delivery.Bll.Mapping;
using Delivery.Bll.Services;
using Delivery.Dal.Abstractions;
using Delivery.Dal.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Логування: Serilog (налаштування — у секції "Serilog" appsettings.json) ----------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));

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
builder.Services.AddScoped<IDishService, DishService>();
builder.Services.AddScoped<IOrderService, OrderService>();

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

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();   // 404 на неіснуючий маршрут теж у форматі ProblemDetails

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Потрібен для інтеграційних тестів через WebApplicationFactory.</summary>
public partial class Program;
