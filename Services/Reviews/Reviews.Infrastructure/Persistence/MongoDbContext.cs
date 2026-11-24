using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence;

/// <summary>Секція "MongoDb" в appsettings.json (рядок підключення, назва БД).</summary>
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    /// <summary>Пул і таймаути — у самому рядку: ?maxPoolSize=50&amp;serverSelectionTimeoutMS=5000</summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    public string DatabaseName { get; set; } = "smakoviz_feedback";
}

/// <summary>
/// Єдина точка доступу до MongoDB (Singleton: MongoClient потокобезпечний і тримає пул з'єднань).
/// Налаштування — через IOptions; колекції — для всіх доменних сутностей (імена як у Лаб. 01).
/// </summary>
public sealed class MongoDbContext
{
    public const string ReviewsCollection = "reviews";
    public const string DiscussionsCollection = "discussions";
    public const string MessagesCollection = "messages";

    public MongoDbContext(IOptions<MongoDbOptions> options)
    {
        var settings = options.Value;
        Client = new MongoClient(settings.ConnectionString);
        Database = Client.GetDatabase(settings.DatabaseName);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public IMongoCollection<Review> Reviews => Database.GetCollection<Review>(ReviewsCollection);
    public IMongoCollection<Discussion> Discussions => Database.GetCollection<Discussion>(DiscussionsCollection);
    public IMongoCollection<Message> Messages => Database.GetCollection<Message>(MessagesCollection);
}
