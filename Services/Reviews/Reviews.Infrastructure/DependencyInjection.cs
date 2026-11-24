using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Domain.Interfaces;
using Reviews.Infrastructure.Persistence;
using Reviews.Infrastructure.Persistence.Queries;
using Reviews.Infrastructure.Persistence.Repositories;
using Reviews.Infrastructure.Seeding;

namespace Reviews.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Реєстрація шару Infrastructure: BSON-мапінг (один раз), налаштування MongoDB через IOptions,
    /// контекст (Singleton), репозиторії й запити читання (Scoped), індекси й сідінг при старті.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MongoMappings.Register();   // до першого звернення до БД

        services.AddOptions<MongoDbOptions>()
            .Bind(configuration.GetSection(MongoDbOptions.SectionName))
            .PostConfigure(options =>
            {
                // .NET Aspire передає рядок підключення як ConnectionStrings__ReviewsDb — він має пріоритет
                var aspire = configuration.GetConnectionString("ReviewsDb");
                if (string.IsNullOrWhiteSpace(aspire)) return;

                options.ConnectionString = aspire;
                try
                {
                    var database = MongoUrl.Create(aspire).DatabaseName;
                    if (!string.IsNullOrWhiteSpace(database))
                        options.DatabaseName = database;
                }
                catch (MongoConfigurationException)
                {
                    // назва БД лишається з секції MongoDb
                }
            });

        services.AddSingleton<MongoDbContext>();

        // запис (Domain-інтерфейси) і читання (Application-інтерфейси) — окремо (CQRS)
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IDiscussionRepository, DiscussionRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<IDiscussionQueries, DiscussionQueries>();

        // старт: спершу індекси, потім дані (hosted services запускаються в порядку реєстрації)
        services.AddSingleton<IDataSeeder, ReviewSeeder>();
        services.AddSingleton<IDataSeeder, DiscussionSeeder>();
        services.AddHostedService<IndexCreationService>();
        services.AddHostedService<DataSeedingHostedService>();

        return services;
    }
}
