using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Enums;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Відгук — корінь агрегату, документ колекції reviews (структура з Лаб. 01).
///   вкладені документи: author (VO Author), aspects (VO RatingAspects);
///   вкладені масиви:    comments (Comment), photos;
///   посилання:          restaurantId, dishId (Меню), orderId (Доставка), discussionId (discussions);
///   гнучка схема:       dishId/dishName — лише для відгуків на страву, aspects — лише на ресторан.
/// </summary>
public class Review : BaseEntity
{
    public const int MaxComments = 50;
    public const int MaxPhotos = 5;
    public const int MaxTextLength = 2000;

    // приватні (не readonly) поля — їх напряму заповнює BSON-серіалізатор через BsonClassMap
    private List<Comment> _comments = [];
    private List<string> _photos = [];

    public ReviewTarget TargetType { get; private set; }
    public long RestaurantId { get; private set; }

    /// <summary>Копія назви ресторану з «Меню» (оновлюється подією RestaurantRenamed, Лаб. 08).</summary>
    public string RestaurantName { get; private set; } = "";

    public long? DishId { get; private set; }
    public string? DishName { get; private set; }
    public long? OrderId { get; private set; }
    public string? OrderNumber { get; private set; }
    public Author Author { get; private set; } = null!;
    public Rating Rating { get; private set; } = null!;
    public string? Text { get; private set; }
    public RatingAspects? Aspects { get; private set; }
    public ObjectId? DiscussionId { get; private set; }

    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();
    public IReadOnlyCollection<string> Photos => _photos.AsReadOnly();

    private Review() { } // для десеріалізації

    public Review(
        ReviewTarget targetType,
        long restaurantId,
        string restaurantName,
        Author author,
        Rating rating,
        string? text = null,
        long? dishId = null,
        string? dishName = null,
        RatingAspects? aspects = null,
        long? orderId = null,
        string? orderNumber = null,
        IEnumerable<string>? photos = null)
    {
        if (restaurantId <= 0) throw new DomainException("RestaurantId must be positive");
        if (string.IsNullOrWhiteSpace(restaurantName)) throw new DomainException("Restaurant name is required");
        if (author.CustomerId is null) throw new DomainException("Only a customer can leave a review");

        if (targetType == ReviewTarget.Dish)
        {
            if (dishId is null or <= 0 || string.IsNullOrWhiteSpace(dishName))
                throw new DomainException("A dish review must reference a dish (dishId and dishName)");
        }
        else if (dishId is not null)
        {
            throw new DomainException("Only a dish review can reference a dish");
        }

        if (aspects is not null && targetType != ReviewTarget.Restaurant)
            throw new DomainException("Aspects can be rated only in a restaurant review");
        if (orderId is <= 0) throw new DomainException("OrderId must be positive");

        TargetType = targetType;
        RestaurantId = restaurantId;
        RestaurantName = restaurantName.Trim();
        DishId = dishId;
        DishName = dishName?.Trim();
        OrderId = orderId;
        OrderNumber = orderNumber;
        Author = author;
        Rating = rating;
        Text = NormalizeText(text);
        Aspects = aspects;

        foreach (var photo in photos ?? [])
        {
            if (string.IsNullOrWhiteSpace(photo)) throw new DomainException("Photo url is required");
            if (_photos.Count >= MaxPhotos) throw new DomainException($"A review can have at most {MaxPhotos} photos");
            _photos.Add(photo.Trim());
        }
    }

    /// <summary>Автор редагує відгук.</summary>
    public void Edit(Rating rating, string? text, RatingAspects? aspects)
    {
        if (aspects is not null && TargetType != ReviewTarget.Restaurant)
            throw new DomainException("Aspects can be rated only in a restaurant review");

        Rating = rating;
        Text = NormalizeText(text);
        Aspects = aspects;
        Touch();
    }

    /// <summary>Коментар (відповідь ресторану, інших клієнтів) — вкладений у документ відгуку.</summary>
    public Comment AddComment(string authorName, string text)
    {
        if (_comments.Count >= MaxComments)
            throw new DomainException($"A review can have at most {MaxComments} comments. Continue in a discussion");

        var comment = new Comment(authorName, text, DateTime.UtcNow);
        _comments.Add(comment);
        Touch();
        return comment;
    }

    public void AddPhoto(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new DomainException("Photo url is required");
        if (_photos.Count >= MaxPhotos) throw new DomainException($"A review can have at most {MaxPhotos} photos");
        _photos.Add(url.Trim());
        Touch();
    }

    /// <summary>Прив'язати обговорення, відкрите довкола цього відгуку.</summary>
    public void AttachDiscussion(ObjectId discussionId)
    {
        if (DiscussionId is not null)
            throw new DomainException("This review already has a discussion");
        DiscussionId = discussionId;
        Touch();
    }

    private static string? NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.Length > MaxTextLength)
            throw new DomainException($"Review text must be at most {MaxTextLength} characters");
        return text;
    }
}

/// <summary>Коментар — вкладений документ масиву comments { authorName, text, createdAt }.</summary>
public sealed class Comment
{
    public const int MaxTextLength = 1000;

    public string AuthorName { get; }
    public string Text { get; }
    public DateTime CreatedAt { get; }

    public Comment(string authorName, string text, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(authorName)) throw new DomainException("Comment author is required");
        if (string.IsNullOrWhiteSpace(text)) throw new DomainException("Comment text is required");
        if (text.Trim().Length > MaxTextLength)
            throw new DomainException($"Comment must be at most {MaxTextLength} characters");

        AuthorName = authorName.Trim();
        Text = text.Trim();
        CreatedAt = createdAt;
    }
}
