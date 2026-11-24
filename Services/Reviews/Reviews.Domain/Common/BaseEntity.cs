using MongoDB.Bson;

namespace Reviews.Domain.Common;

/// <summary>
/// Базова сутність, адаптована для MongoDB:
///   Id        — ObjectId (генерується в застосунку, тож Id відомий ще до вставки);
///   CreatedAt / UpdatedAt — аудит;
///   Version   — номер версії документа для оптимістичної конкурентності:
///               оновлення в репозиторії застосовується лише до документа з очікуваною версією.
/// Setter-и захищені: змінити стан можна лише методами сутності.
/// </summary>
public abstract class BaseEntity
{
    public ObjectId Id { get; protected set; } = ObjectId.GenerateNewId();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public int Version { get; protected set; }

    /// <summary>Позначити зміну (викликають методи сутності).</summary>
    protected void Touch() => UpdatedAt = DateTime.UtcNow;

    /// <summary>Збільшити версію — викликає репозиторій перед збереженням.</summary>
    public void BumpVersion() => Version++;
}
