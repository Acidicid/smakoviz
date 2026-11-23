namespace Menu.Domain.Common;

/// <summary>
/// Аудит, м'яке видалення та оптимістична конкурентність (як у схемі Лаб. 01).
/// Setter-и приватні: значення проставляє MenuDbContext.SaveChangesAsync через Change Tracker,
/// а не код застосунку.
/// </summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    /// <summary>М'яке видалення: рядок лишається в БД, але глобальний фільтр EF його ховає.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>ROWVERSION: якщо рядок змінив хтось інший, SaveChanges кине конфлікт (→ 409).</summary>
    public byte[] RowVersion { get; private set; } = [];
}
