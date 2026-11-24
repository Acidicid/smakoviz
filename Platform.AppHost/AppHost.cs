// =====================================================================
// SmakoViz · .NET Aspire AppHost — вся система однією командою (SQL Server, MongoDB, delivery-api, menu-api, reviews-api).
//   dotnet run --project Platform.AppHost
// Відкрий посилання на Dashboard з консолі: там SQL Server і delivery-api мають бути Running.
// =====================================================================

var builder = DistributedApplication.CreateBuilder(args);

// SQL Server у контейнері + volume, щоб дані переживали перезапуск.
// Ім'я бази-ресурсу "DeliveryDb" = ім'я рядка підключення в Delivery.Api (ConnectionStrings:DeliveryDb),
// тому Aspire сам передасть його в API через змінну ConnectionStrings__DeliveryDb — код API не змінюється.
var sql = builder.AddSqlServer("sql")
    .WithDataVolume("smakoviz-delivery-sql")
    .WithLifetime(ContainerLifetime.Persistent);   // контейнер не перестворюється при кожному запуску

// Кожен мікросервіс — СВОЯ база (Лаб. 01), хоч і на одному сервері SQL у розробці.
var deliveryDb = sql.AddDatabase("DeliveryDb", databaseName: "SmakoViz_Delivery");   // Проєкт №1 — ADO.NET + Dapper
var menuDb = sql.AddDatabase("MenuDb", databaseName: "SmakoViz_Menu");               // Проєкт №2 — EF Core

// Варіант 1 (без контейнера Aspire): використати вже запущену базу з Лаб. 01 (db/deploy.ps1):
//   var deliveryDb = builder.AddConnectionString("DeliveryDb");
// і прописати рядок у appsettings.json цього проєкту: "ConnectionStrings": { "DeliveryDb": "..." }

builder.AddProject<Projects.Delivery_Api>("delivery-api")
    .WithReference(deliveryDb)
    .WaitFor(deliveryDb)                              // API стартує, коли база готова
    .WithEnvironment("Database__Initialize", "true")  // застосувати schema/procedures/seed з Лаб. 01
    .WithExternalHttpEndpoints();

// Сервіс «Меню» (Лаб. 03): ім'я ресурсу "MenuDb" = ConnectionStrings:MenuDb у Menu.Api;
// схему створюють міграції EF Core (MigrateAsync при старті), дані — HasData + DbInitializer.
builder.AddProject<Projects.Menu_Api>("menu-api")
    .WithReference(menuDb)
    .WaitFor(menuDb)
    .WithExternalHttpEndpoints();

// Сервіс «Відгуки» (Лаб. 04, Clean Architecture + CQRS): MongoDB у контейнері.
// Ім'я ресурсу "ReviewsDb" → ConnectionStrings__ReviewsDb, який Reviews.Infrastructure ставить
// поверх секції MongoDb з appsettings. Індекси й seed-дані сервіс створює сам при старті.
var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume("smakoviz-reviews-mongo")
    .WithLifetime(ContainerLifetime.Persistent);

var reviewsDb = mongo.AddDatabase("ReviewsDb", databaseName: "smakoviz_feedback");

builder.AddProject<Projects.Reviews_WebApi>("reviews-api")
    .WithReference(reviewsDb)
    .WaitFor(reviewsDb)
    .WithExternalHttpEndpoints();

builder.Build().Run();
