using Platform.EventBus;

namespace Delivery.Api.IntegrationEvents;

// ---------- Подія, яку ПУБЛІКУЄ «Доставка» ----------

/// <summary>
/// Замовлення оформлено (транзакцію збережено). Самодостатня: «Меню» списує порції без запиту до «Доставки».
/// Без адреси й телефону — персональні дані іншим сервісам не потрібні. Routing key: delivery.order.placed.
/// </summary>
public sealed record OrderPlacedIntegrationEvent(
    long OrderId,
    string OrderNumber,
    long RestaurantId,
    decimal TotalAmount,
    IReadOnlyList<OrderPlacedLine> Lines) : IntegrationEvent;

public sealed record OrderPlacedLine(long DishId, int Quantity);

// ---------- Події «Меню», які «Доставка» СПОЖИВАЄ (власна копія контракту) ----------

/// <summary>Страву змінено в «Меню» → оновити локальний довідник dbo.Dishes. Routing key: menu.dish.updated.</summary>
public sealed record DishUpdatedIntegrationEvent(
    long DishId,
    long RestaurantId,
    string RestaurantName,
    string Name,
    decimal Price,
    bool IsAvailable,
    int AvailablePortions) : IntegrationEvent;

/// <summary>Страву видалено з меню → у локальному довіднику вона стає недоступною. Routing key: menu.dish.deleted.</summary>
public sealed record DishDeletedIntegrationEvent(long DishId, long RestaurantId) : IntegrationEvent;

public static class DeliveryRoutingKeys
{
    public const string OrderPlaced = "delivery.order.placed";
    public const string DishUpdated = "menu.dish.updated";
    public const string DishDeleted = "menu.dish.deleted";
    public const string Queue = "delivery-api.events";
}
