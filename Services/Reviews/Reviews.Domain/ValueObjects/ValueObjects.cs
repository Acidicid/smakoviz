using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Enums;
using Reviews.Domain.Exceptions;

namespace Reviews.Domain.ValueObjects;

/// <summary>
/// Автор відгуку — вкладений документ author { customerId, displayName }.
/// DisplayName — копія імені з сервісу «Доставка» (політика дублювання, Лаб. 01).
/// </summary>
public sealed class Author : ValueObject
{
    public long? CustomerId { get; }
    public string DisplayName { get; }

    public Author(long? customerId, string displayName)
    {
        if (customerId is <= 0)
            throw new DomainException("CustomerId must be positive");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Author display name is required");
        if (displayName.Trim().Length > 100)
            throw new DomainException("Author display name must be at most 100 characters");

        CustomerId = customerId;
        DisplayName = displayName.Trim();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CustomerId;
        yield return DisplayName;
    }
}

/// <summary>
/// Оцінка 1–5. Окремий тип замість int: неможливо створити оцінку 10 — правило живе в одному місці.
/// У БД зберігається просто числом (rating: 5) — власний BSON-серіалізатор в Infrastructure.
/// </summary>
public sealed class Rating : ValueObject
{
    public const int Min = 1;
    public const int Max = 5;

    public int Value { get; }

    public Rating(int value)
    {
        if (value is < Min or > Max)
            throw new DomainException($"Rating must be between {Min} and {Max}");
        Value = value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Оцінки ресторану за аспектами — вкладений документ aspects { food, packaging, portionSize }.
/// Кожен аспект необов'язковий, але хоча б один має бути задано.
/// </summary>
public sealed class RatingAspects : ValueObject
{
    public int? Food { get; }
    public int? Packaging { get; }
    public int? PortionSize { get; }

    public RatingAspects(int? food, int? packaging, int? portionSize)
    {
        Check(food, nameof(Food));
        Check(packaging, nameof(Packaging));
        Check(portionSize, nameof(PortionSize));
        if (food is null && packaging is null && portionSize is null)
            throw new DomainException("At least one aspect must be rated");

        Food = food;
        Packaging = packaging;
        PortionSize = portionSize;
    }

    private static void Check(int? value, string name)
    {
        if (value is < Rating.Min or > Rating.Max)
            throw new DomainException($"{name} must be between {Rating.Min} and {Rating.Max}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Food;
        yield return Packaging;
        yield return PortionSize;
    }
}

/// <summary>Автор повідомлення в обговоренні: author { customerId?, displayName, role }.</summary>
public sealed class MessageAuthor : ValueObject
{
    public long? CustomerId { get; }
    public string DisplayName { get; }
    public MessageRole Role { get; }

    public MessageAuthor(long? customerId, string displayName, MessageRole role)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Author display name is required");
        if (role == MessageRole.Customer && customerId is null or <= 0)
            throw new DomainException("A customer message must have a customer id");

        CustomerId = customerId;
        DisplayName = displayName.Trim();
        Role = role;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CustomerId;
        yield return DisplayName;
        yield return Role;
    }
}

/// <summary>
/// Прев'ю повідомлення, вбудоване в обговорення (гібридна модель з Лаб. 01:
/// останні 3 повідомлення — всередині discussions, повна історія — в колекції messages).
/// </summary>
public sealed class MessagePreview : ValueObject
{
    public ObjectId MessageId { get; }
    public string AuthorName { get; }
    public string Text { get; }
    public DateTime CreatedAt { get; }

    public MessagePreview(ObjectId messageId, string authorName, string text, DateTime createdAt)
    {
        if (messageId == ObjectId.Empty) throw new DomainException("Message id is required");
        MessageId = messageId;
        AuthorName = authorName;
        // у прев'ю — не більше 200 символів
        Text = text.Length <= 200 ? text : text[..197] + "...";
        CreatedAt = createdAt;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MessageId;
        yield return AuthorName;
        yield return Text;
        yield return CreatedAt;
    }
}
