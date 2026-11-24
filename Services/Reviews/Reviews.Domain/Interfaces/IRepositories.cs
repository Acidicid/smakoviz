using MongoDB.Bson;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;

namespace Reviews.Domain.Interfaces;

// Контракти доступу до даних оголошені ТУТ, у внутрішньому шарі;
// реалізація на MongoDB.Driver — у Reviews.Infrastructure. Domain про драйвер не знає.

public interface IReviewRepository
{
    Task<Review?> GetByIdAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Чи вже залишав цей клієнт відгук на цю ціль (страву / ресторан / кур'єра ресторану).</summary>
    Task<bool> ExistsForTargetAsync(long customerId, ReviewTarget target, long restaurantId, long? dishId,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(ObjectId id, CancellationToken ct = default);

    Task AddAsync(Review review, CancellationToken ct = default);

    /// <summary>Оновлення з перевіркою Version (оптимістична конкурентність): чужа зміна → конфлікт.</summary>
    Task UpdateAsync(Review review, CancellationToken ct = default);

    /// <summary>Видалення; якщо expectedVersion задано — лише документа саме цієї версії.</summary>
    Task<bool> DeleteAsync(ObjectId id, int? expectedVersion, CancellationToken ct = default);
}

public interface IDiscussionRepository
{
    Task<Discussion?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task AddAsync(Discussion discussion, CancellationToken ct = default);
    Task UpdateAsync(Discussion discussion, CancellationToken ct = default);
}

public interface IMessageRepository
{
    Task AddAsync(Message message, CancellationToken ct = default);
}
