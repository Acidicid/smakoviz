namespace Platform.Aggregator.Models;

// Копії ЛИШЕ тих полів відповідей сервісів, які потрібні Aggregator (System.Text.Json ігнорує решту).
// Project reference на сервіси свідомо немає: Aggregator залежить від контракту HTTP, а не від їхнього коду.

// ---------- delivery-api: GET /api/delivery/orders/{id} ----------
public sealed record DeliveryOrderDto(
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
    IReadOnlyList<DeliveryOrderLineDto> Lines);

public sealed record DeliveryOrderLineDto(long DishId, string DishName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record DeliveryOrderSummaryDto(long Id, string OrderNumber, string RestaurantName, string Status, DateTime PlacedAt, decimal TotalAmount);

// ---------- menu-api: GET /api/menu/restaurants/{id} ----------
public sealed record MenuRestaurantDto(
    long Id,
    string Name,
    string City,
    string CuisineType,
    bool IsActive,
    MenuRestaurantProfileDto? Profile,
    IReadOnlyList<MenuDishDto> Dishes);

public sealed record MenuRestaurantProfileDto(string Address, string Phone, TimeOnly OpensAt, TimeOnly ClosesAt, decimal MinOrderAmount);

public sealed record MenuDishDto(long Id, string Name, decimal Price, bool IsAvailable, int AvailablePortions);

// ---------- reviews-api ----------
public sealed record ReviewsRatingDto(long RestaurantId, int ReviewsCount, double AverageRating);

public sealed record ReviewsPageDto(IReadOnlyList<ReviewItemDto> Items, long TotalCount);

public sealed record ReviewItemDto(
    string Id,
    string TargetType,
    long RestaurantId,
    long? DishId,
    string? DishName,
    string AuthorName,
    int Rating,
    string? Text,
    DateTime CreatedAt);

// спільна форма сторінки для delivery-api / menu-api
public sealed record PageDto<T>(IReadOnlyList<T> Items, long TotalCount);
