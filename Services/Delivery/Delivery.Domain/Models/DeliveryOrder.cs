namespace Delivery.Domain.Models;

/// <summary>Корінь агрегату «Замовлення доставки» (dbo.DeliveryOrders).</summary>
public class DeliveryOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public long CustomerId { get; set; }

    /// <summary>Посилання на ресторан у БД Меню — без FOREIGN KEY.</summary>
    public long RestaurantId { get; set; }

    /// <summary>Знімок назви ресторану на момент оформлення.</summary>
    public string RestaurantName { get; set; } = "";

    public string Status { get; set; } = OrderStatus.Placed;
    public DateTime PlacedAt { get; set; }
    public decimal ItemsTotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal TotalAmount { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>1:1 — адреса й контакти доставки.</summary>
    public DeliveryDetails? Details { get; set; }

    /// <summary>M:N з Dishes через OrderLines.</summary>
    public List<OrderLine> Lines { get; set; } = [];

    /// <summary>1:N — журнал переходів статусу.</summary>
    public List<OrderStatusChange> History { get; set; } = [];
}

/// <summary>dbo.DeliveryDetails — 1:1 із замовленням (OrderId — водночас PK і FK).</summary>
public class DeliveryDetails
{
    public long OrderId { get; set; }
    public string Address { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string? CourierNote { get; set; }
    public DateTime? DesiredTime { get; set; }
}

/// <summary>dbo.OrderLines — позиція замовлення. DishName і UnitPrice — ЗНІМОК на момент замовлення.</summary>
public class OrderLine
{
    public long OrderId { get; set; }
    public long DishId { get; set; }
    public string DishName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? Comment { get; set; }

    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>dbo.OrderStatusHistory — один перехід статусу.</summary>
public class OrderStatusChange
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public string? ChangedBy { get; set; }
    public string? Reason { get; set; }
}
