namespace Platform.Aggregator.Models;

// Композитні DTO відображають БІЗНЕС-СЦЕНАРІЙ (картка замовлення / картка ресторану),
// а не копіюють структури окремих сервісів. Null-поля не серіалізуються (WhenWritingNull),
// тож якщо сервіс недоступний — блоку просто немає, а причина — у Warnings.

/// <summary>GET /api/aggregator/orders/{id}/full — повна картка замовлення.</summary>
public sealed record FullOrderDto(
    OrderCardDto Order,
    RestaurantCardDto? Restaurant,
    IReadOnlyList<OrderItemCardDto> Items,
    IReadOnlyList<ReviewCardDto> Reviews,
    IReadOnlyList<string> Warnings);

public sealed record OrderCardDto(
    long Id,
    string OrderNumber,
    long CustomerId,
    string Status,
    DateTime PlacedAt,
    decimal ItemsTotal,
    decimal DeliveryFee,
    decimal TotalAmount);

/// <summary>Позиція замовлення + АКТУАЛЬНІ дані страви з меню (ціна зараз, чи ще продається).</summary>
public sealed record OrderItemCardDto(
    long DishId,
    string DishName,
    int Quantity,
    decimal PriceInOrder,
    decimal? CurrentPrice,
    bool? AvailableNow,
    double? DishRating);

public sealed record RestaurantCardDto(
    long Id,
    string Name,
    string City,
    string CuisineType,
    bool IsActive,
    string? Address,
    string? Phone,
    string? WorkingHours,
    double? AverageRating,
    int? ReviewsCount);

public sealed record ReviewCardDto(
    string Id,
    string TargetType,
    string? DishName,
    string AuthorName,
    int Rating,
    string? Text,
    DateTime CreatedAt);

/// <summary>GET /api/aggregator/restaurants/{id} — картка ресторану: меню + рейтинг + останні відгуки.</summary>
public sealed record RestaurantOverviewDto(
    RestaurantCardDto Restaurant,
    IReadOnlyList<MenuDishDto> Menu,
    IReadOnlyList<ReviewCardDto> LatestReviews,
    IReadOnlyList<string> Warnings);
