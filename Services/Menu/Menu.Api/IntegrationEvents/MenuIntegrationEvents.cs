using Platform.EventBus;

namespace Menu.Api.IntegrationEvents;

// ---------- Події, які ПУБЛІКУЄ «Меню» (сервіс-власник страв) ----------

/// <summary>
/// Страву створено або змінено (ціна, доступність, порції). Самодостатня: споживачі оновлюють свої копії
/// без запиту до «Меню» — «Доставка» (локальний довідник страв), «Відгуки» (денормалізована назва).
/// Routing key: menu.dish.updated.
/// </summary>
public sealed record DishUpdatedIntegrationEvent(
    long DishId,
    long RestaurantId,
    string RestaurantName,
    string Name,
    decimal Price,
    bool IsAvailable,
    int AvailablePortions) : IntegrationEvent;

/// <summary>Страву видалено з меню. Routing key: menu.dish.deleted.</summary>
public sealed record DishDeletedIntegrationEvent(long DishId, long RestaurantId) : IntegrationEvent;

// ---------- Події, які «Меню» СПОЖИВАЄ (власна копія контракту «Доставки» — зв'язок лише через JSON) ----------

/// <summary>Оформлено замовлення → списати порції. Routing key: delivery.order.placed.</summary>
public sealed record OrderPlacedIntegrationEvent(
    long OrderId,
    string OrderNumber,
    long RestaurantId,
    IReadOnlyList<OrderPlacedLine> Lines) : IntegrationEvent;

public sealed record OrderPlacedLine(long DishId, int Quantity);

public static class MenuRoutingKeys
{
    public const string DishUpdated = "menu.dish.updated";
    public const string DishDeleted = "menu.dish.deleted";
    public const string OrderPlaced = "delivery.order.placed";
    public const string Queue = "menu-api.events";
}
