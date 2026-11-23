namespace Delivery.Domain.Models;

/// <summary>
/// Статуси замовлення — ті самі рядки, що в CHECK-обмеженні CK_DeliveryOrders_Status (Лаб. 01).
/// Допустимі переходи перевіряє процедура dbo.usp_Order_ChangeStatus:
///   Placed → Confirmed → Paid → Cooking → OnTheWay → Delivered;
///   Cancelled — лише з Placed або Confirmed.
/// </summary>
public static class OrderStatus
{
    public const string Placed = "Placed";
    public const string Confirmed = "Confirmed";
    public const string Paid = "Paid";
    public const string Cooking = "Cooking";
    public const string OnTheWay = "OnTheWay";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All =
        [Placed, Confirmed, Paid, Cooking, OnTheWay, Delivered, Cancelled];

    public static bool IsKnown(string? status) =>
        status is not null && All.Contains(status, StringComparer.Ordinal);
}
