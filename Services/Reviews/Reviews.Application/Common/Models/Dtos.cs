namespace Reviews.Application.Common.Models;

// DTO — класи з get/set: їх заповнює і AutoMapper (з доменних сутностей у командах),
// і MongoDB Driver напряму (проєкція $project у IReviewQueries). Id — рядковий ObjectId.

public sealed class AuthorDto
{
    public long? CustomerId { get; set; }
    public string DisplayName { get; set; } = "";
}

public sealed class AspectsDto
{
    public int? Food { get; set; }
    public int? Packaging { get; set; }
    public int? PortionSize { get; set; }
}

public sealed class CommentDto
{
    public string AuthorName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Відгук повністю (GET /api/reviews/{id}).</summary>
public sealed class ReviewDetailsDto
{
    public string Id { get; set; } = "";
    public string TargetType { get; set; } = "";
    public long RestaurantId { get; set; }
    public string RestaurantName { get; set; } = "";
    public long? DishId { get; set; }
    public string? DishName { get; set; }
    public long? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public AuthorDto Author { get; set; } = new();
    public int Rating { get; set; }
    public string? Text { get; set; }
    public AspectsDto? Aspects { get; set; }
    public List<string> Photos { get; set; } = [];
    public List<CommentDto> Comments { get; set; } = [];
    public string? DiscussionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }
}

/// <summary>Рядок списку відгуків (лише потрібні поля — проєкція в MongoDB).</summary>
public sealed class ReviewListItemDto
{
    public string Id { get; set; } = "";
    public string TargetType { get; set; } = "";
    public long RestaurantId { get; set; }
    public string RestaurantName { get; set; } = "";
    public long? DishId { get; set; }
    public string? DishName { get; set; }
    public string AuthorName { get; set; } = "";
    public int Rating { get; set; }
    public string? Text { get; set; }
    public int CommentsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Version { get; set; }
}

/// <summary>Підсумкова оцінка ресторану (aggregation pipeline: $match → $group).</summary>
public sealed class RestaurantRatingDto
{
    public long RestaurantId { get; set; }
    public string? RestaurantName { get; set; }
    public int ReviewsCount { get; set; }
    public double AverageRating { get; set; }
    public List<TargetRatingDto> ByTarget { get; set; } = [];
}

public sealed class TargetRatingDto
{
    public string TargetType { get; set; } = "";
    public int Count { get; set; }
    public double AverageRating { get; set; }
}

public sealed class MessagePreviewDto
{
    public string MessageId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class DiscussionDto
{
    public string Id { get; set; } = "";
    public long RestaurantId { get; set; }
    public string RestaurantName { get; set; } = "";
    public string? ReviewId { get; set; }
    public string Title { get; set; } = "";
    public int MessagesCount { get; set; }
    public List<MessagePreviewDto> RecentMessages { get; set; } = [];
    public DateTime LastActivityAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class MessageDto
{
    public string Id { get; set; } = "";
    public string DiscussionId { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string? ReplyTo { get; set; }
    public DateTime CreatedAt { get; set; }
}
