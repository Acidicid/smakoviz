using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence.Queries;

public sealed class DiscussionQueries(MongoDbContext context) : IDiscussionQueries
{
    private static readonly BsonDocument DiscussionProjection = new()
    {
        { "_id", new BsonDocument("$toString", "$_id") },
        { "restaurantId", 1 }, { "restaurantName", 1 },
        { "reviewId", new BsonDocument("$toString", "$reviewId") },
        { "title", 1 }, { "messagesCount", 1 },
        // вбудоване прев'ю: ObjectId кожного повідомлення → рядок ($map по масиву)
        { "recentMessages", new BsonDocument("$map", new BsonDocument
            {
                { "input", new BsonDocument("$ifNull", new BsonArray { "$recentMessages", new BsonArray() }) },
                { "as", "m" },
                { "in", new BsonDocument
                    {
                        { "messageId", new BsonDocument("$toString", "$$m.messageId") },
                        { "authorName", "$$m.authorName" },
                        { "text", "$$m.text" },
                        { "createdAt", "$$m.createdAt" }
                    }
                }
            })
        },
        { "lastActivityAt", 1 }, { "createdAt", 1 }, { "version", 1 }
    };

    private static readonly BsonDocument MessageProjection = new()
    {
        { "_id", new BsonDocument("$toString", "$_id") },
        { "discussionId", new BsonDocument("$toString", "$discussionId") },
        { "authorName", "$author.displayName" },
        { "role", "$author.role" },
        { "text", 1 },
        { "replyTo", new BsonDocument("$toString", "$replyTo") },
        { "createdAt", 1 }
    };

    public async Task<DiscussionDto?> GetByIdAsync(string id, CancellationToken ct = default)
        => await context.Discussions.Aggregate()
            .Match(Builders<Discussion>.Filter.Eq("_id", MongoMappings.ToObjectId(id)))
            .Project<DiscussionDto>(DiscussionProjection)
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<MessageDto>> GetMessagesAsync(string discussionId, int page, int pageSize,
        CancellationToken ct = default)
    {
        // індекс ix_messages_discussion_created { discussionId: 1, createdAt: 1 }
        var filter = Builders<Message>.Filter.Eq("discussionId", MongoMappings.ToObjectId(discussionId));
        var total = await context.Messages.CountDocumentsAsync(filter, cancellationToken: ct);

        var items = await context.Messages.Aggregate()
            .Match(filter)
            .Sort(Builders<Message>.Sort.Ascending("createdAt").Ascending("_id"))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .Project<MessageDto>(MessageProjection)
            .ToListAsync(ct);

        return new PagedResult<MessageDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }
}
