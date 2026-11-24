using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Model;

// =====================================================================
// SmakoViz · API Gateway (YARP). Аналогія: адміністратор на вході до фудкорту —
// клієнт підходить до ОДНОГО вікна (порт 5000), а адміністратор знає, до якої кухні нести замовлення
// (/api/delivery → delivery-api, /api/menu → menu-api, /api/reviews → reviews-api, /api/aggregator → aggregator),
// видає номер чека (X-Correlation-Id) і записує в журнал, куди й скільки часу ходив запит.
// =====================================================================
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, health, service discovery, CorrelationIdHandler

// ---------- YARP: маршрути з appsettings.json, адреси http://<resource> → service discovery Aspire ----------
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();   // http://menu-api → реальна адреса від Aspire

// ---------- JWT (підготовка до автентифікації) ----------
// Токен перевіряється на шлюзі; маршрут вимагає його, лише якщо в ReverseProxy:Routes:<id>:AuthorizationPolicy
// вказано "authenticated". За замовчуванням політика "default" (anonymous) — лабораторні запити йдуть без токена.
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"] ?? new string('x', 32))),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

var app = builder.Build();

// 1) CorrelationId — ДО MapReverseProxy: генерується тут (якщо клієнт не надіслав власний),
//    записується в Request.Headers → YARP копіює заголовок у проксований запит.
app.UseMiddleware<CorrelationIdMiddleware>();

// 2) Логування шлюзу: рішення маршрутизації, статус, тривалість, розмір, IP клієнта
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    await next();
    sw.Stop();

    // Features.Get, а не GetReverseProxyFeature(): для /health і "/" YARP-маршруту немає
    var proxy = context.Features.Get<IReverseProxyFeature>();
    var route = proxy?.Route.Config.RouteId ?? "(none)";
    var cluster = proxy?.Cluster?.Config.ClusterId ?? "(none)";
    var destination = proxy?.ProxiedDestination?.Model.Config.Address;

    app.Logger.LogInformation(
        "Gateway {Method} {Path} from {ClientIp} -> route {Route}, cluster {Cluster} ({Destination}): {StatusCode} in {ElapsedMs} ms, {ResponseBytes} bytes",
        context.Request.Method, context.Request.Path.Value, context.Connection.RemoteIpAddress?.ToString(),
        route, cluster, destination, context.Response.StatusCode, sw.ElapsedMilliseconds, context.Response.ContentLength);
});

app.UseAuthentication();
app.UseAuthorization();

// 3) YARP + помилки недоступних сервісів (timeout, connection refused) з деталями
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use(async (context, next) =>
    {
        await next();

        var error = context.GetForwarderErrorFeature();
        if (error is not null)
        {
            var proxy = context.GetReverseProxyFeature();
            app.Logger.LogError(error.Exception,
                "Gateway failed to reach cluster {Cluster} for {Method} {Path}: {ForwarderError}",
                proxy.Cluster?.Config.ClusterId, context.Request.Method, context.Request.Path.Value, error.Error);
        }
    });
});

app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new
{
    service = "SmakoViz API Gateway",
    routes = new[] { "/api/delivery/**", "/api/menu/**", "/api/reviews/**", "/api/aggregator/**" }
})).ExcludeFromDescription();

app.Run();
