using System.ComponentModel.DataAnnotations;

namespace Delivery.Bll.Dtos;

// ---------------- вихідні DTO ----------------

public record DeliveryDetailsDto(string Address, string ContactPhone, string? CourierNote, DateTime? DesiredTime);

/// <summary>Позиція замовлення: DishName і UnitPrice — знімок на момент замовлення.</summary>
public record OrderLineDto(long DishId, string DishName, decimal UnitPrice, int Quantity, decimal LineTotal, string? Comment);

public record OrderStatusChangeDto(string? FromStatus, string ToStatus, DateTime ChangedAt, string? ChangedBy, string? Reason);

/// <summary>Повне замовлення (GET /api/orders/{id}).</summary>
public record OrderDto(
    long Id,
    string OrderNumber,
    long CustomerId,
    long RestaurantId,
    string RestaurantName,
    string Status,
    DateTime PlacedAt,
    decimal ItemsTotal,
    decimal DeliveryFee,
    decimal TotalAmount,
    string RowVersion,
    DeliveryDetailsDto? Details,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<OrderStatusChangeDto> History);

/// <summary>Рядок списку замовлень (пошук з пагінацією).</summary>
public record OrderSummaryDto(
    long Id,
    string OrderNumber,
    long CustomerId,
    string RestaurantName,
    string Status,
    DateTime PlacedAt,
    decimal TotalAmount);

// ---------------- вхідні DTO ----------------

/// <summary>Оформлення замовлення (POST /api/orders) — транзакційна операція на 3 репозиторії.</summary>
public class PlaceOrderDto
{
    [Range(1, long.MaxValue)]
    public long CustomerId { get; set; }

    /// <summary>
    /// Знімок назви ресторану. Власник — контекст «Меню»; у Лаб. 05 її підставлятиме
    /// Gateway/Aggregator, поки що передає клієнт.
    /// </summary>
    [Required, StringLength(150, MinimumLength = 1)]
    public string RestaurantName { get; set; } = "";

    [Required, StringLength(300, MinimumLength = 5)]
    public string Address { get; set; } = "";

    [Required, RegularExpression(@"^\+380\d{9}$", ErrorMessage = "Phone must look like +380XXXXXXXXX")]
    public string ContactPhone { get; set; } = "";

    [StringLength(500)]
    public string? CourierNote { get; set; }

    /// <summary>Бажаний час доставки (UTC); null — «якнайшвидше».</summary>
    public DateTime? DesiredTime { get; set; }

    [Required, MinLength(1), MaxLength(30)]
    public List<PlaceOrderItemDto> Items { get; set; } = [];
}

public class PlaceOrderItemDto
{
    [Range(1, long.MaxValue)]
    public long DishId { get; set; }

    [Range(1, 50)]
    public int Quantity { get; set; }

    [StringLength(200)]
    public string? Comment { get; set; }
}

/// <summary>Довантажити страву в замовлення в статусі Placed.</summary>
public class AddOrderLineDto : PlaceOrderItemDto;

/// <summary>Зміна статусу (кур'єр, ресторан, оплата).</summary>
public class ChangeOrderStatusDto
{
    /// <summary>Placed, Confirmed, Paid, Cooking, OnTheWay, Delivered, Cancelled.</summary>
    [Required]
    public string Status { get; set; } = "";

    [StringLength(300)]
    public string? Reason { get; set; }

    /// <summary>Необов'язково: RowVersion з останнього GET — тоді паралельна зміна дасть 409.</summary>
    public string? RowVersion { get; set; }
}

public class CancelOrderDto
{
    [StringLength(300)]
    public string? Reason { get; set; }
}
