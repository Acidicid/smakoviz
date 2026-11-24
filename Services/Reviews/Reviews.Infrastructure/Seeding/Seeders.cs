using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Seeding;

/// <summary>Сідер однієї колекції. Ідемпотентний: якщо дані вже є — нічого не робить.</summary>
public interface IDataSeeder
{
    /// <summary>Порядок виконання (спершу ті, на кого посилаються інші).</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken ct = default);
}

/// <summary>
/// Відгуки з Лаб. 01 (db/p3/seed.js). Id ресторанів/страв = Меню, customerId/orderId = Доставка.
/// Документи створюються доменними конструкторами (з усіма перевірками) і пишуться одним InsertManyAsync.
/// </summary>
public sealed class ReviewSeeder(MongoDbContext context, ILogger<ReviewSeeder> logger) : IDataSeeder
{
    public int Order => 1;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await context.Reviews.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            logger.LogInformation("Seed skipped: {Collection} is not empty", MongoDbContext.ReviewsCollection);
            return;
        }

        var olena = new Author(1, "Олена К.");
        var andrii = new Author(2, "Андрій М.");
        var iryna = new Author(3, "Ірина Б.");

        var banush = new Review(ReviewTarget.Dish, 1, "Буковинська хата", olena, new Rating(5),
            "Бануш як у Карпатах, бринза солона в міру. Порція велика.",
            dishId: 101, dishName: "Бануш з бринзою і шкварками", orderId: 1, orderNumber: "SV-000001",
            photos: ["reviews/a01-1.jpg", "reviews/a01-2.jpg"]);
        banush.AddComment("Буковинська хата", "Дякуємо! Чекаємо знову 🙂");
        banush.AddComment("Андрій М.", "Підтверджую, топ");

        var reviews = new List<Review>
        {
            banush,
            new(ReviewTarget.Dish, 1, "Буковинська хата", olena, new Rating(4),
                "Смачний, але пампушки трохи охололи.",
                dishId: 102, dishName: "Борщ з пампушками", orderId: 1, orderNumber: "SV-000001"),
            new(ReviewTarget.Restaurant, 1, "Буковинська хата", olena, new Rating(5),
                "Усе гаряче, упаковка не протекла.",
                aspects: new RatingAspects(5, 4, 5), orderId: 1, orderNumber: "SV-000001"),
            new(ReviewTarget.Courier, 1, "Буковинська хата", olena, new Rating(5),
                "Ввічливий кур'єр, подзвонив заздалегідь.", orderId: 1, orderNumber: "SV-000001"),
            new(ReviewTarget.Dish, 2, "Pizza Pronto", andrii, new Rating(3),
                dishId: 201, dishName: "Піца Маргарита 30 см"),
            new(ReviewTarget.Restaurant, 3, "Суші Дзен", iryna, new Rating(2),
                "Довго підтверджували замовлення, тому скасувала.",
                aspects: new RatingAspects(3, 2, null))
        };

        await context.Reviews.InsertManyAsync(reviews, cancellationToken: ct);   // bulk-вставка
        logger.LogInformation("Seeded {Count} documents into {Collection}", reviews.Count, MongoDbContext.ReviewsCollection);
    }
}

/// <summary>Обговорення + повідомлення (гібридна модель). Іде після відгуків — посилається на них.</summary>
public sealed class DiscussionSeeder(MongoDbContext context, ILogger<DiscussionSeeder> logger) : IDataSeeder
{
    public int Order => 2;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await context.Discussions.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            logger.LogInformation("Seed skipped: {Collection} is not empty", MongoDbContext.DiscussionsCollection);
            return;
        }

        var banushReview = await context.Reviews
            .Find(Builders<Review>.Filter.Eq("dishId", 101L))
            .FirstOrDefaultAsync(ct);

        var discussion = new Discussion(1, "Буковинська хата", "Питання про бануш", banushReview?.Id);
        var messages = new List<Message>
        {
            new(discussion.Id, new MessageAuthor(1, "Олена К.", MessageRole.Customer),
                "А бануш готують на кукурудзяній крупі чи на манці?"),
            new(discussion.Id, new MessageAuthor(null, "Буковинська хата", MessageRole.Restaurant),
                "На кукурудзяній, з домашньою сметаною."),
            new(discussion.Id, new MessageAuthor(2, "Андрій М.", MessageRole.Customer),
                "А можна без шкварок?"),
            new(discussion.Id, new MessageAuthor(null, "Буковинська хата", MessageRole.Restaurant),
                "Так, напишіть у коментарі до позиції.")
        };
        foreach (var message in messages)
            discussion.RegisterMessage(message);   // лічильник + прев'ю останніх 3

        await context.Discussions.InsertOneAsync(discussion, cancellationToken: ct);
        await context.Messages.InsertManyAsync(messages, cancellationToken: ct);

        if (banushReview is not null && banushReview.DiscussionId is null)
        {
            banushReview.AttachDiscussion(discussion.Id);
            banushReview.BumpVersion();
            await context.Reviews.ReplaceOneAsync(r => r.Id == banushReview.Id, banushReview, cancellationToken: ct);
        }

        logger.LogInformation("Seeded 1 discussion with {Count} messages", messages.Count);
    }
}

/// <summary>
/// Запускає всі IDataSeeder при старті (після IndexCreationService — він зареєстрований раніше).
/// Вимикається налаштуванням MongoDb:Seed=false; у Production за замовчуванням вимкнено.
/// </summary>
public sealed class DataSeedingHostedService(
    IEnumerable<IDataSeeder> seeders,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DataSeedingHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var enabled = configuration.GetValue("MongoDb:Seed", !environment.IsProduction());
        if (!enabled)
        {
            logger.LogInformation("MongoDB seeding is disabled");
            return;
        }

        foreach (var seeder in seeders.OrderBy(s => s.Order))
        {
            try
            {
                await seeder.SeedAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Seeder {Seeder} failed", seeder.GetType().Name);
                return;
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
