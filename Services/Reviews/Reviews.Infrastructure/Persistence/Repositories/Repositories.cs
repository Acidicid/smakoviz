using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Exceptions;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.Interfaces;

namespace Reviews.Infrastructure.Persistence.Repositories;

/// <summary>
/// Репозиторій відгуків на MongoDB Driver (асинхронний API).
/// ОПТИМІСТИЧНА КОНКУРЕНТНІСТЬ: ReplaceOne з фільтром { _id, version: очікувана }.
/// Якщо між читанням і записом документ змінив хтось інший, version у БД уже більша —
/// фільтр не знайде документ (MatchedCount = 0) → ConflictException → 409.
/// </summary>
public sealed class ReviewRepository(MongoDbContext context) : IReviewRepository
{
    private readonly IMongoCollection<Review> _reviews = context.Reviews;

    public async Task<Review?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
        => await _reviews.Find(r => r.Id == id).FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(ObjectId id, CancellationToken ct = default)
        => _reviews.Find(r => r.Id == id).AnyAsync(ct);

    public Task<bool> ExistsForTargetAsync(long customerId, ReviewTarget target, long restaurantId, long? dishId,
        CancellationToken ct = default)
    {
        // ті самі поля, що в unique-індексі ux_reviews_author_target
        var f = Builders<Review>.Filter;
        var filter = f.Eq("author.customerId", customerId)
                     & f.Eq(r => r.TargetType, target)
                     & f.Eq(r => r.RestaurantId, restaurantId)
                     & (dishId is null ? f.Exists("dishId", false) : f.Eq(r => r.DishId, dishId));
        return _reviews.Find(filter).AnyAsync(ct);
    }

    public Task AddAsync(Review review, CancellationToken ct = default)
        => _reviews.InsertOneAsync(review, cancellationToken: ct);

    public async Task UpdateAsync(Review review, CancellationToken ct = default)
    {
        var expectedVersion = review.Version;
        review.BumpVersion();

        var result = await _reviews.ReplaceOneAsync(
            r => r.Id == review.Id && r.Version == expectedVersion,
            review,
            cancellationToken: ct);

        if (result.MatchedCount == 0)
            throw new ConflictException(
                $"Review {review.Id} was modified by someone else (expected version {expectedVersion}). Reload and try again");
    }

    public async Task<bool> DeleteAsync(ObjectId id, int? expectedVersion, CancellationToken ct = default)
    {
        var f = Builders<Review>.Filter;
        var filter = f.Eq(r => r.Id, id);
        if (expectedVersion is not null)
            filter &= f.Eq(r => r.Version, expectedVersion.Value);

        var result = await _reviews.DeleteOneAsync(filter, ct);
        return result.DeletedCount > 0;
    }
}

public sealed class DiscussionRepository(MongoDbContext context) : IDiscussionRepository
{
    private readonly IMongoCollection<Discussion> _discussions = context.Discussions;

    public async Task<Discussion?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
        => await _discussions.Find(d => d.Id == id).FirstOrDefaultAsync(ct);

    public Task AddAsync(Discussion discussion, CancellationToken ct = default)
        => _discussions.InsertOneAsync(discussion, cancellationToken: ct);

    public async Task UpdateAsync(Discussion discussion, CancellationToken ct = default)
    {
        var expectedVersion = discussion.Version;
        discussion.BumpVersion();

        var result = await _discussions.ReplaceOneAsync(
            d => d.Id == discussion.Id && d.Version == expectedVersion,
            discussion,
            cancellationToken: ct);

        if (result.MatchedCount == 0)
            throw new ConflictException($"Discussion {discussion.Id} was modified by someone else");
    }
}

public sealed class MessageRepository(MongoDbContext context) : IMessageRepository
{
    public Task AddAsync(Message message, CancellationToken ct = default)
        => context.Messages.InsertOneAsync(message, cancellationToken: ct);
}
