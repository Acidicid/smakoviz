namespace Delivery.Domain.Models;

/// <summary>Клієнт сервісу доставки (таблиця dbo.Customers). Власник даних — цей контекст.</summary>
public class Customer
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }

    // аудит і м'яке видалення
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>ROWVERSION — для оптимістичної конкурентності.</summary>
    public byte[] RowVersion { get; set; } = [];
}
