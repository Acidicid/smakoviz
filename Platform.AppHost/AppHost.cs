// =====================================================================
// SmakoViz · .NET Aspire AppHost — вся платформа однією командою:
//   SQL Server + MongoDB + Redis → delivery-api, menu-api, reviews-api → aggregator → api-gateway (http://localhost:5000)
//   dotnet run --project Platform.AppHost
// Порядок запуску задають ланцюжки WaitFor: БД → сервіси → Aggregator → Gateway.
// =====================================================================

var builder = DistributedApplication.CreateBuilder(args);

// SQL Server у контейнері + volume, щоб дані переживали перезапуск.
// Ім'я бази-ресурсу "DeliveryDb" = ім'я рядка підключення в Delivery.Api (ConnectionStrings:DeliveryDb),
// тому Aspire сам передасть його в API через змінну ConnectionStrings__DeliveryDb — код API не змінюється.
// Пароль SQL Server — СЕКРЕТНИЙ параметр (Лаб. 05): не в коді, а в user secrets AppHost
// ("Parameters:sql-password") або вводиться в Dashboard. У Dashboard значення приховане.
var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", password: sqlPassword)
    .WithDataVolume("smakoviz-delivery-sql")
    .WithLifetime(ContainerLifetime.Persistent);   // контейнер не перестворюється при кожному запуску

// ---------- Лаб. 06: Redis — розподілений кеш (L2) для сервісів і Aggregator ----------
// Рядок підключення потрапляє в сервіси як ConnectionStrings:redis (адреса в appsettings не потрібна).
// Volume — кеш переживає перезапуск контейнера.
var redis = builder.AddRedis("redis")
    .WithDataVolume("smakoviz-redis")
    .WithLifetime(ContainerLifetime.Persistent);

// Кожен мікросервіс — СВОЯ база (Лаб. 01), хоч і на одному сервері SQL у розробці.
var deliveryDb = sql.AddDatabase("DeliveryDb", databaseName: "SmakoViz_Delivery");   // Проєкт №1 — ADO.NET + Dapper
var menuDb = sql.AddDatabase("MenuDb", databaseName: "SmakoViz_Menu");               // Проєкт №2 — EF Core

// Варіант 1 (без контейнера Aspire): використати вже запущену базу з Лаб. 01 (db/deploy.ps1):
//   var deliveryDb = builder.AddConnectionString("DeliveryDb");
// і прописати рядок у appsettings.json цього проєкту: "ConnectionStrings": { "DeliveryDb": "..." }

// launchProfileName "https": HTTPS-endpoint з HTTP/1.1 + HTTP/2 (ALPN) — потрібен для gRPC (Лаб. 06);
// REST через Gateway іде на http-endpoint того самого сервісу.
var delivery = builder.AddProject<Projects.Delivery_Api>("delivery-api", launchProfileName: "https")
    .WithReference(deliveryDb)
    .WaitFor(deliveryDb)
    .WithReference(redis)
    .WaitFor(redis)                              // API стартує, коли база готова
    .WithEnvironment("Database__Initialize", "true")  // застосувати schema/procedures/seed з Лаб. 01
    .WithExternalHttpEndpoints();

// Сервіс «Меню» (Лаб. 03): ім'я ресурсу "MenuDb" = ConnectionStrings:MenuDb у Menu.Api;
// схему створюють міграції EF Core (MigrateAsync при старті), дані — HasData + DbInitializer.
var menu = builder.AddProject<Projects.Menu_Api>("menu-api", launchProfileName: "https")
    .WithReference(menuDb)
    .WaitFor(menuDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

// Сервіс «Відгуки» (Лаб. 04, Clean Architecture + CQRS): MongoDB у контейнері.
// Ім'я ресурсу "ReviewsDb" → ConnectionStrings__ReviewsDb, який Reviews.Infrastructure ставить
// поверх секції MongoDb з appsettings. Індекси й seed-дані сервіс створює сам при старті.
var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume("smakoviz-reviews-mongo")
    .WithLifetime(ContainerLifetime.Persistent);

var reviewsDb = mongo.AddDatabase("ReviewsDb", databaseName: "smakoviz_feedback");

var reviews = builder.AddProject<Projects.Reviews_WebApi>("reviews-api", launchProfileName: "https")
    .WithReference(reviewsDb)
    .WaitFor(reviewsDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

// ---------- Лаб. 05: Aggregator + API Gateway ----------
// WithReference(сервіс) → service discovery: у коді Aggregator/Gateway адреса — це ім'я ресурсу
// ("https+http://menu-api", "http://menu-api"), а реальний порт підставляє Aspire.
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
    .WithReference(delivery).WithReference(menu).WithReference(reviews)
    .WaitFor(delivery).WaitFor(menu).WaitFor(reviews)
    .WithReference(redis).WaitFor(redis);   // Лаб. 06: кеш агрегованих відповідей

// Gateway — єдина точка входу з ФІКСОВАНИМ портом 5000; стартує останнім.
builder.AddProject<Projects.Platform_ApiGateway>("api-gateway")
    .WithHttpEndpoint(port: 5000, name: "public")
    .WithReference(delivery).WithReference(menu).WithReference(reviews)
    .WithReference(aggregator)
    .WaitFor(aggregator)
    .WithExternalHttpEndpoints();

builder.Build().Run();
