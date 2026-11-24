using Grpc.Core;
using Platform.Aggregator.Models;
using DeliveryGrpc = Delivery.Grpc;
using MenuGrpc = Menu.Grpc;
using ReviewsGrpc = Reviews.Grpc;

namespace Platform.Aggregator.Clients;

/// <summary>
/// gRPC-реалізація (Лаб. 06). Клієнти згенеровані з .proto сервісів і зареєстровані через AddGrpcClient
/// з адресою за логічним іменем ("https://menu-api") — service discovery, resilience і CorrelationIdHandler
/// діють так само, як для HttpClient: X-Correlation-Id потрапляє в МЕТАДАНІ кожного gRPC-виклику.
/// Усі виклики йдуть по одному HTTP/2-з'єднанню на сервіс (мультиплексування) — паралельні запити не відкривають
/// нових з'єднань. Кожен виклик має deadline і CancellationToken.
/// NOT_FOUND → null; UNAVAILABLE / DEADLINE_EXCEEDED тощо → RpcException (стане попередженням).
/// </summary>
public sealed class GrpcPlatformClient(
    DeliveryGrpc.DeliveryService.DeliveryServiceClient delivery,
    MenuGrpc.MenuService.MenuServiceClient menu,
    ReviewsGrpc.ReviewsService.ReviewsServiceClient reviews) : IPlatformClient
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    public Transport Transport => Transport.Grpc;

    public async Task<DeliveryOrderDto?> GetOrderAsync(long orderId, CancellationToken ct)
    {
        var reply = await OrNull(() => delivery.GetOrderAsync(
            new DeliveryGrpc.GetOrderRequest { Id = orderId }, deadline: DeadlineFromNow(), cancellationToken: ct).ResponseAsync);
        if (reply is null) return null;

        return new DeliveryOrderDto(
            reply.Id, reply.OrderNumber, reply.CustomerId, reply.RestaurantId, reply.RestaurantName, reply.Status,
            reply.PlacedAt?.ToDateTime() ?? default,
            ToDecimal(reply.ItemsTotal), ToDecimal(reply.DeliveryFee), ToDecimal(reply.TotalAmount),
            reply.Lines.Select(l => new DeliveryOrderLineDto(
                l.DishId, l.DishName, ToDecimal(l.UnitPrice), l.Quantity, ToDecimal(l.LineTotal))).ToList());
    }

    public async Task<MenuRestaurantDto?> GetRestaurantAsync(long restaurantId, bool onlyAvailable, CancellationToken ct)
    {
        var reply = await OrNull(() => menu.GetRestaurantAsync(
            new MenuGrpc.GetRestaurantRequest { Id = restaurantId, OnlyAvailable = onlyAvailable },
            deadline: DeadlineFromNow(), cancellationToken: ct).ResponseAsync);
        if (reply is null) return null;

        return new MenuRestaurantDto(
            reply.Id, reply.Name, reply.City, reply.CuisineType, reply.IsActive,
            reply.Profile is null ? null : new MenuRestaurantProfileDto(
                reply.Profile.Address, reply.Profile.Phone,
                TimeOnly.Parse(reply.Profile.OpensAt), TimeOnly.Parse(reply.Profile.ClosesAt),
                ToDecimal(reply.Profile.MinOrderAmount)),
            reply.Dishes.Select(d => new MenuDishDto(
                d.Id, d.Name, ToDecimal(d.Price), d.IsAvailable, d.AvailablePortions)).ToList());
    }

    public async Task<ReviewsRatingDto?> GetRestaurantRatingAsync(long restaurantId, CancellationToken ct)
    {
        var reply = await OrNull(() => reviews.GetRestaurantRatingAsync(
            new ReviewsGrpc.GetRestaurantRatingRequest { RestaurantId = restaurantId },
            deadline: DeadlineFromNow(), cancellationToken: ct).ResponseAsync);
        return reply is null ? null : new ReviewsRatingDto(reply.RestaurantId, reply.ReviewsCount, reply.AverageRating);
    }

    public Task<IReadOnlyList<ReviewItemDto>?> GetLatestReviewsAsync(long restaurantId, int count, CancellationToken ct)
        => ListAsync(new ReviewsGrpc.ListReviewsRequest { RestaurantId = restaurantId, PageSize = count }, ct);

    public Task<IReadOnlyList<ReviewItemDto>?> GetCustomerReviewsAsync(long restaurantId, long customerId, CancellationToken ct)
        => ListAsync(new ReviewsGrpc.ListReviewsRequest { RestaurantId = restaurantId, CustomerId = customerId, PageSize = 50 }, ct);

    private async Task<IReadOnlyList<ReviewItemDto>?> ListAsync(ReviewsGrpc.ListReviewsRequest request, CancellationToken ct)
    {
        var reply = await OrNull(() => reviews.ListReviewsAsync(request, deadline: DeadlineFromNow(), cancellationToken: ct).ResponseAsync);
        return reply?.Reviews.Select(r => new ReviewItemDto(
            r.Id, r.TargetType, r.RestaurantId,
            r.HasDishId ? r.DishId : null,
            r.HasDishName ? r.DishName : null,
            r.AuthorName, r.Rating,
            r.HasText ? r.Text : null,
            r.CreatedAt?.ToDateTime() ?? default)).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private static DateTime DeadlineFromNow() => DateTime.UtcNow.Add(Deadline);

    /// <summary>NOT_FOUND — це відповідь «немає такого», а не збій сервісу → null.</summary>
    private static async Task<T?> OrNull<T>(Func<Task<T>> call) where T : class
    {
        try
        {
            return await call();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    private static decimal ToDecimal(DeliveryGrpc.DecimalValue? v) => v is null ? 0m : v.Units + v.Nanos / 1_000_000_000m;
    private static decimal ToDecimal(MenuGrpc.DecimalValue? v) => v is null ? 0m : v.Units + v.Nanos / 1_000_000_000m;
}
