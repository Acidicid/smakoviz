using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

/// <summary>Транспорт до мікросервісів. gRPC — основний (Лаб. 06), HTTP — з Лаб. 05, лишений для порівняння затримок.</summary>
public enum Transport { Grpc, Http }

/// <summary>
/// Єдиний інтерфейс доступу Aggregator до трьох мікросервісів — AggregationService не знає, gRPC це чи HTTP.
/// Контракт: «не знайдено» → null; недоступність сервісу → виняток (RpcException / HttpRequestException),
/// який AggregationService перетворює на попередження (часткова відповідь).
/// </summary>
public interface IPlatformClient
{
    Transport Transport { get; }

    Task<DeliveryOrderDto?> GetOrderAsync(long orderId, CancellationToken ct);
    Task<MenuRestaurantDto?> GetRestaurantAsync(long restaurantId, bool onlyAvailable, CancellationToken ct);
    Task<ReviewsRatingDto?> GetRestaurantRatingAsync(long restaurantId, CancellationToken ct);
    Task<IReadOnlyList<ReviewItemDto>?> GetLatestReviewsAsync(long restaurantId, int count, CancellationToken ct);
    Task<IReadOnlyList<ReviewItemDto>?> GetCustomerReviewsAsync(long restaurantId, long customerId, CancellationToken ct);
}

/// <summary>HTTP-реалізація поверх typed HttpClients з Лаб. 05 (ServiceClients.cs).</summary>
public sealed class HttpPlatformClient(DeliveryClient delivery, MenuClient menu, ReviewsClient reviews) : IPlatformClient
{
    public Transport Transport => Transport.Http;

    public Task<DeliveryOrderDto?> GetOrderAsync(long orderId, CancellationToken ct) => delivery.GetOrderAsync(orderId, ct);

    public Task<MenuRestaurantDto?> GetRestaurantAsync(long restaurantId, bool onlyAvailable, CancellationToken ct)
        => menu.GetRestaurantAsync(restaurantId, onlyAvailable, ct);

    public Task<ReviewsRatingDto?> GetRestaurantRatingAsync(long restaurantId, CancellationToken ct)
        => reviews.GetRestaurantRatingAsync(restaurantId, ct);

    public Task<IReadOnlyList<ReviewItemDto>?> GetLatestReviewsAsync(long restaurantId, int count, CancellationToken ct)
        => reviews.GetLatestForRestaurantAsync(restaurantId, count, ct);

    public Task<IReadOnlyList<ReviewItemDto>?> GetCustomerReviewsAsync(long restaurantId, long customerId, CancellationToken ct)
        => reviews.GetForCustomerAsync(restaurantId, customerId, ct);
}
