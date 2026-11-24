using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Обговорення — документ колекції discussions. ГІБРИДНА модель з Лаб. 01:
/// останні 3 повідомлення вбудовані (recentMessages — швидке прев'ю без JOIN),
/// повна історія — окремі документи в колекції messages (посилання discussionId).
/// </summary>
public class Discussion : BaseEntity
{
    public const int PreviewSize = 3;

    private List<MessagePreview> _recentMessages = [];

    public long RestaurantId { get; private set; }
    public string RestaurantName { get; private set; } = "";
    public ObjectId? ReviewId { get; private set; }
    public string Title { get; private set; } = "";
    public int MessagesCount { get; private set; }
    public DateTime LastActivityAt { get; private set; }

    public IReadOnlyCollection<MessagePreview> RecentMessages => _recentMessages.AsReadOnly();

    private Discussion() { } // для десеріалізації

    public Discussion(long restaurantId, string restaurantName, string title, ObjectId? reviewId = null)
    {
        if (restaurantId <= 0) throw new DomainException("RestaurantId must be positive");
        if (string.IsNullOrWhiteSpace(restaurantName)) throw new DomainException("Restaurant name is required");
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Title is required");
        if (title.Trim().Length > 200) throw new DomainException("Title must be at most 200 characters");

        RestaurantId = restaurantId;
        RestaurantName = restaurantName.Trim();
        Title = title.Trim();
        ReviewId = reviewId;
        LastActivityAt = CreatedAt;
    }

    /// <summary>
    /// Зареєструвати нове повідомлення: лічильник +1, прев'ю — лише останні 3.
    /// Сам документ повідомлення зберігається в колекції messages.
    /// </summary>
    public void RegisterMessage(Message message)
    {
        if (message.DiscussionId != Id)
            throw new DomainException("Message belongs to another discussion");

        MessagesCount++;
        _recentMessages.Add(new MessagePreview(message.Id, message.Author.DisplayName, message.Text, message.CreatedAt));
        if (_recentMessages.Count > PreviewSize)
            _recentMessages.RemoveRange(0, _recentMessages.Count - PreviewSize);

        LastActivityAt = message.CreatedAt;
        Touch();
    }
}
