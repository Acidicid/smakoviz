using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence.Queries;

/// <summary>
/// Сторона ЧИТАННЯ (CQRS) для відгуків. Доменні сутності тут не матеріалізуються:
/// aggregation pipeline робить $match (фільтри) → $sort → $skip/$limit (пагінація) → $project
/// одразу у форму DTO — з БД повертаються лише потрібні поля потрібної сторінки.
/// </summary>
public sealed class ReviewQueries(MongoDbContext context) : IReviewQueries
{
    private readonly IMongoCollection<Review> _reviews = context.Reviews;

    /// <summary>Проєкція в ReviewDetailsDto: ObjectId → рядок ($toString), решта полів — як є.</summary>
    private static readonly BsonDocument DetailsProjection = new()
    {
        { "_id", new BsonDocument("$toString", "$_id") },
        { "targetType", 1 }, { "restaurantId", 1 }, { "restaurantName", 1 },
        { "dishId", 1 }, { "dishName", 1 }, { "orderId", 1 }, { "orderNumber", 1 },
        { "author", 1 }, { "rating", 1 }, { "text", 1 }, { "aspects", 1 },
        { "photos", 1 }, { "comments", 1 },
        { "discussionId", new BsonDocument("$toString", "$discussionId") },
        { "createdAt", 1 }, { "updatedAt", 1 }, { "version", 1 }
    };

    /// <summary>Проєкція в ReviewListItemDto: без масивів — лише ім'я автора й кількість коментарів.</summary>
    private static readonly BsonDocument ListProjection = new()
    {
        { "_id", new BsonDocument("$toString", "$_id") },
        { "targetType", 1 }, { "restaurantId", 1 }, { "restaurantName", 1 },
        { "dishId", 1 }, { "dishName", 1 },
        { "authorName", "$author.displayName" },
        { "rating", 1 }, { "text", 1 },
        { "commentsCount", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$comments", new BsonArray() })) },
        { "createdAt", 1 }, { "version", 1 }
    };

    public async Task<ReviewDetailsDto?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _reviews.Aggregate()
            .Match(Builders<Review>.Filter.Eq("_id", MongoMappings.ToObjectId(id)))
            .Project<ReviewDetailsDto>(DetailsProjection)
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<ReviewListItemDto>> GetListAsync(ReviewListFilter q, CancellationToken ct = default)
    {
        var filter = BuildFilter(q);

        // сортування підкріплене складеними індексами: { restaurantId: 1, createdAt: -1 }, { dishId: 1, rating: -1 }
        var sortBuilder = Builders<Review>.Sort;
        var sort = q.Descending
            ? sortBuilder.Combine(sortBuilder.Descending(q.SortBy), sortBuilder.Descending("_id"))
            : sortBuilder.Combine(sortBuilder.Ascending(q.SortBy), sortBuilder.Ascending("_id"));

        var total = await _reviews.CountDocumentsAsync(filter, cancellationToken: ct);

        var items = await _reviews.Aggregate()
            .Match(filter)                         // $match
            .Sort(sort)                            // $sort
            .Skip((q.Page - 1) * q.PageSize)       // $skip
            .Limit(q.PageSize)                     // $limit
            .Project<ReviewListItemDto>(ListProjection)  // $project → DTO
            .ToListAsync(ct);

        return new PagedResult<ReviewListItemDto>
        {
            Items = items, TotalCount = total, Page = q.Page, PageSize = q.PageSize
        };
    }

    public async Task<RestaurantRatingDto> GetRestaurantRatingAsync(long restaurantId, CancellationToken ct = default)
    {
        // $match { restaurantId } → $group за targetType: кількість, середня оцінка
        var groups = await _reviews.Aggregate()
            .Match(Builders<Review>.Filter.Eq(r => r.RestaurantId, restaurantId))
            .Group(new BsonDocument
            {
                { "_id", "$targetType" },
                { "count", new BsonDocument("$sum", 1) },
                { "average", new BsonDocument("$avg", "$rating") },
                { "restaurantName", new BsonDocument("$last", "$restaurantName") }
            })
            .ToListAsync(ct);

        var byTarget = groups
            .Select(g => new TargetRatingDto
            {
                TargetType = g["_id"].AsString,
                Count = g["count"].ToInt32(),
                AverageRating = Math.Round(g["average"].ToDouble(), 2)
            })
            .OrderBy(t => t.TargetType)
            .ToList();

        var totalCount = byTarget.Sum(t => t.Count);
        return new RestaurantRatingDto
        {
            RestaurantId = restaurantId,
            RestaurantName = groups.FirstOrDefault()?["restaurantName"].AsString,
            ReviewsCount = totalCount,
            AverageRating = totalCount == 0
                ? 0
                : Math.Round(byTarget.Sum(t => t.AverageRating * t.Count) / totalCount, 2),
            ByTarget = byTarget
        };
    }

    private static FilterDefinition<Review> BuildFilter(ReviewListFilter q)
    {
        var f = Builders<Review>.Filter;
        var filter = f.Empty;

        if (q.RestaurantId is { } restaurantId) filter &= f.Eq(r => r.RestaurantId, restaurantId);
        if (q.DishId is { } dishId) filter &= f.Eq("dishId", dishId);
        if (q.TargetType is { } target) filter &= f.Eq("targetType", target);
        if (q.MinRating is { } minRating) filter &= f.Gte("rating", minRating);
        if (q.CustomerId is { } customerId) filter &= f.Eq("author.customerId", customerId);
        if (q.Search is { } search) filter &= f.Text(search);   // text-індекс tx_reviews_text

        return filter;
    }
}
