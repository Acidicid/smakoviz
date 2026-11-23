using FluentValidation;
using Menu.Api.Infrastructure;
using Menu.Bll.Mapping;
using Menu.Bll.Services;
using Menu.Bll.Validation;
using Menu.Dal.Context;
using Menu.Dal.Repositories;
using Menu.Dal.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ---------- Serilog: структуровані логи (властивості як ключі), sink — консоль ----------
// Рівні й override-и — у секції "Serilog" appsettings.json. SQL-команди EF Core логуються на рівні
// Information (Microsoft.EntityFrameworkCore.Database.Command) — видно, що Eager = 1 запит, Explicit = кілька.
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "menu-api")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}"));

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
builder.Services.AddScoped<IRestaurantService, RestaurantService>();
builder.Services.AddScoped<IDishService, DishService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();

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

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    // додаткові структуровані властивості в рядку «HTTP GET /api/dishes responded 200 in 12 ms»
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
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

/// <summary>Для інтеграційних тестів (WebApplicationFactory).</summary>
public partial class Program;
