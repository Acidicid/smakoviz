using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Повідомлення обговорення — окремий документ колекції messages (повідомлень може бути тисячі,
/// тому вони не вбудовані в обговорення, а посилаються на нього через discussionId).
/// </summary>
public class Message : BaseEntity
{
    public const int MaxTextLength = 2000;

    public ObjectId DiscussionId { get; private set; }
    public MessageAuthor Author { get; private set; } = null!;
    public string Text { get; private set; } = "";
    public ObjectId? ReplyTo { get; private set; }

    private Message() { } // для десеріалізації

    public Message(ObjectId discussionId, MessageAuthor author, string text, ObjectId? replyTo = null)
    {
        if (discussionId == ObjectId.Empty) throw new DomainException("Discussion is required");
        if (string.IsNullOrWhiteSpace(text)) throw new DomainException("Message text is required");
        if (text.Trim().Length > MaxTextLength)
            throw new DomainException($"Message must be at most {MaxTextLength} characters");

        DiscussionId = discussionId;
        Author = author;
        Text = text.Trim();
        ReplyTo = replyTo;
    }
}
