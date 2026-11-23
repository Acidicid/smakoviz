// =====================================================================
// SmakoViz · .NET Aspire AppHost — вся система однією командою.
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

var deliveryDb = sql.AddDatabase("DeliveryDb", databaseName: "SmakoViz_Delivery");

// Варіант 1 (без контейнера Aspire): використати вже запущену базу з Лаб. 01 (db/deploy.ps1):
//   var deliveryDb = builder.AddConnectionString("DeliveryDb");
// і прописати рядок у appsettings.json цього проєкту: "ConnectionStrings": { "DeliveryDb": "..." }

builder.AddProject<Projects.Delivery_Api>("delivery-api")
    .WithReference(deliveryDb)
    .WaitFor(deliveryDb)                              // API стартує, коли база готова
    .WithEnvironment("Database__Initialize", "true")  // застосувати schema/procedures/seed з Лаб. 01
    .WithExternalHttpEndpoints();

builder.Build().Run();
