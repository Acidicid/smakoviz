using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Seeding;

/// <summary>
/// Створює індекси при старті застосунку (кожен — під конкретний запит).
/// Повторне створення наявного індексу з тими самими ключами й параметрами безпечне — MongoDB його не дублює.
/// Імена й параметри збігаються з db/p3/collections.js (Лаб. 01), тож сервіс працює і з базою з Лаб. 01.
/// </summary>
public sealed class IndexCreationService(MongoDbContext context, ILogger<IndexCreationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await CreateReviewIndexesAsync(ct);
            await CreateDiscussionIndexesAsync(ct);
            logger.LogInformation("MongoDB indexes are ready");
        }
        catch (Exception ex)
        {
            // не валимо застосунок: без БД API відповідатиме 503, а health check покаже проблему
            logger.LogError(ex, "Failed to create MongoDB indexes");
        }
    }

    private Task CreateReviewIndexesAsync(CancellationToken ct)
    {
        var keys = Builders<Review>.IndexKeys;
        return context.Reviews.Indexes.CreateManyAsync(
        [
            // складений: «останні відгуки ресторану» (GET /api/reviews?restaurantId=1)
            new CreateIndexModel<Review>(
                keys.Ascending("restaurantId").Descending("createdAt"),
                new CreateIndexOptions { Name = "ix_reviews_restaurant_created" }),

            // складений + partial: «найкращі відгуки на страву» (лише документи з dishId)
            new CreateIndexModel<Review>(
                keys.Ascending("dishId").Descending("rating"),
                new CreateIndexOptions<Review>
                {
                    Name = "ix_reviews_dish_rating",
                    PartialFilterExpression = Builders<Review>.Filter.Exists("dishId")
                }),

            // «мої відгуки»
            new CreateIndexModel<Review>(
                keys.Ascending("author.customerId").Descending("createdAt"),
                new CreateIndexOptions { Name = "ix_reviews_author" }),

            // UNIQUE: один відгук від клієнта на ту саму ціль (страву / ресторан / кур'єра ресторану)
            new CreateIndexModel<Review>(
                keys.Ascending("author.customerId").Ascending("targetType")
                    .Ascending("restaurantId").Ascending("dishId"),
                new CreateIndexOptions { Name = "ux_reviews_author_target", Unique = true }),

            // UNIQUE + partial: один відгук на ціль у межах замовлення (з Лаб. 01)
            new CreateIndexModel<Review>(
                keys.Ascending("orderId").Ascending("targetType").Ascending("dishId"),
                new CreateIndexOptions<Review>
                {
                    Name = "ux_reviews_order_target",
                    Unique = true,
                    PartialFilterExpression = Builders<Review>.Filter.Exists("orderId")
                }),

            // TEXT: повнотекстовий пошук (?search=...). Українська без стемера → мова "none"
            new CreateIndexModel<Review>(
                keys.Text("text"),
                new CreateIndexOptions { Name = "tx_reviews_text", DefaultLanguage = "none" })
        ], ct);
    }

    private async Task CreateDiscussionIndexesAsync(CancellationToken ct)
    {
        await context.Discussions.Indexes.CreateOneAsync(
            new CreateIndexModel<Discussion>(
                Builders<Discussion>.IndexKeys.Ascending("restaurantId").Descending("lastActivityAt"),
                new CreateIndexOptions { Name = "ix_discussions_restaurant_activity" }),
            cancellationToken: ct);

        // головний запит до messages — історія обговорення з пагінацією
        await context.Messages.Indexes.CreateOneAsync(
            new CreateIndexModel<Message>(
                Builders<Message>.IndexKeys.Ascending("discussionId").Ascending("createdAt"),
                new CreateIndexOptions { Name = "ix_messages_discussion_created" }),
            cancellationToken: ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Для перевірки в README: db.reviews.getIndexes().</summary>
    public static BsonDocument ListIndexesCommand => new("listIndexes", MongoDbContext.ReviewsCollection);
}
