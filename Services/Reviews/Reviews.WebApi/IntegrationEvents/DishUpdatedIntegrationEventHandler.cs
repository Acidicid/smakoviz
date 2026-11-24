using MongoDB.Bson;
using MongoDB.Driver;
using Platform.EventBus;
using Reviews.Application.Caching;
using Reviews.Infrastructure.Persistence;

namespace Reviews.WebApi.IntegrationEvents;

/// <summary>Власна копія контракту «Меню» (зв'язок лише через JSON). Routing key: menu.dish.updated.</summary>
public sealed record DishUpdatedIntegrationEvent(
    long DishId,
    long RestaurantId,
    string RestaurantName,
    string Name,
    decimal Price,
    bool IsAvailable,
    int AvailablePortions) : IntegrationEvent;

public static class ReviewsRoutingKeys
{
    public const string DishUpdated = "menu.dish.updated";
    public const string Queue = "reviews-api.events";
}

/// <summary>
/// Реплікація за політикою дублювання з Лаб. 01: відгук зберігає ДЕНОРМАЛІЗОВАНУ назву страви (dishName),
/// щоб список відгуків читався одним запитом. Змінилась назва в «Меню» → оновлюємо її в УСІХ відгуках цієї страви
/// одним UpdateMany. Ціна й доступність відгукам не потрібні — їх ігноруємо.
/// Ідемпотентність: колекція processed_events з _id = EventId (унікальний ключ). Окремий MongoDB без replica set
/// не має транзакцій, тому порядок такий: UpdateMany (сам по собі ідемпотентний — ставить те саме значення) →
/// позначка «оброблено». Повторна доставка не змінить нічого навіть між цими кроками.
/// </summary>
public sealed class DishUpdatedIntegrationEventHandler(
    MongoDbContext context,
    ITwoLevelCache cache,
    ILogger<DishUpdatedIntegrationEventHandler> logger) : IIntegrationEventHandler<DishUpdatedIntegrationEvent>
{
    private const string ProcessedEventsCollection = "processed_events";

    public async Task HandleAsync(DishUpdatedIntegrationEvent e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Name))
            throw new PermanentEventException($"Event {e.EventId} has an empty dish name");

        var processed = context.Database.GetCollection<BsonDocument>(ProcessedEventsCollection);
        var eventKey = e.EventId.ToString();
        if (await processed.Find(Builders<BsonDocument>.Filter.Eq("_id", eventKey)).AnyAsync(ct))
        {
            logger.LogInformation("Event {EventId} already processed — skipping dish {DishId}", e.EventId, e.DishId);
            return;
        }

        var reviews = context.Database.GetCollection<BsonDocument>(MongoDbContext.ReviewsCollection);
        var filter = Builders<BsonDocument>.Filter.Eq("dishId", e.DishId)
                     & Builders<BsonDocument>.Filter.Ne("dishName", e.Name);   // лише ті, де назва справді інша

        var affectedIds = await reviews.Find(filter).Project(Builders<BsonDocument>.Projection.Include("_id"))
            .ToListAsync(ct);

        long modified = 0;
        if (affectedIds.Count > 0)
        {
            var result = await reviews.UpdateManyAsync(filter, Builders<BsonDocument>.Update
                .Set("dishName", e.Name)
                .Set("updatedAt", DateTime.UtcNow)
                .Inc("version", 1), cancellationToken: ct);   // версія росте → If-Match клієнтів із застарілою копією отримає 409
            modified = result.ModifiedCount;

            await cache.InvalidateAsync("DishRenamedEvent", ct,
                affectedIds.Select(d => ReviewsCacheKeys.Review(d["_id"].AsObjectId.ToString())).ToArray());
        }

        try
        {
            await processed.InsertOneAsync(new BsonDocument
            {
                ["_id"] = eventKey,
                ["eventType"] = nameof(DishUpdatedIntegrationEvent),
                ["processedAt"] = DateTime.UtcNow
            }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // паралельна доставка вже поставила позначку — зміни ідентичні, все гаразд
        }

        logger.LogInformation("Dish {DishId} renamed to {DishName}: updated {ModifiedCount} reviews", e.DishId, e.Name, modified);
    }
}
