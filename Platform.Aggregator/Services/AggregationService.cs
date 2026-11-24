using System.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>
/// Aggregator Pattern. Аналогія: офіціант, який замість трьох походів (на кухню, до бару, до каси)
/// одночасно відправляє три записки й приносить усе на одній таці. Якщо бар зачинений — несе їжу
/// й чек, а на таці записка «напої тимчасово недоступні» (Warnings), а не повертається з порожніми руками (500).
/// </summary>
public sealed class AggregationService(
    DeliveryClient delivery,
    MenuClient menu,
    ReviewsClient reviews,
    ILogger<AggregationService> logger)
{
    public static readonly ActivitySource ActivitySource = new("SmakoViz.Aggregator");
    private const int LatestReviewsCount = 3;

    /// <summary>
    /// Повна картка замовлення: замовлення (delivery-api) → ПАРАЛЕЛЬНО ресторан з актуальним меню (menu-api),
    /// рейтинг ресторану і відгуки цього клієнта (reviews-api).
    /// null — замовлення не знайдене (без нього агрегувати нічого).
    /// </summary>
    public async Task<FullOrderDto?> GetFullOrderAsync(long orderId, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("AggregateFullOrder");
        activity?.SetTag("order.id", orderId);

        var warnings = new List<string>();

        // 1) замовлення — обов'язкове; якщо delivery-api недоступний, повертаємо 503 у контролері
        var order = await SafeAsync(DeliveryClient.ServiceName, $"order {orderId}",
            () => delivery.GetOrderAsync(orderId, ct), warnings);
        if (order is null)
        {
            if (warnings.Count > 0) throw new DependencyUnavailableException(DeliveryClient.ServiceName);
            return null;
        }

        // 2) незалежні виклики — одночасно (Task.WhenAll), CancellationToken передається в кожен
        var restaurantTask = SafeAsync(MenuClient.ServiceName, $"restaurant {order.RestaurantId}",
            () => menu.GetRestaurantAsync(order.RestaurantId, onlyAvailable: false, ct), warnings);
        var ratingTask = SafeAsync(ReviewsClient.ServiceName, $"rating of restaurant {order.RestaurantId}",
            () => reviews.GetRestaurantRatingAsync(order.RestaurantId, ct), warnings);
        var reviewsTask = SafeAsync(ReviewsClient.ServiceName, $"reviews of customer {order.CustomerId}",
            () => reviews.GetForCustomerAsync(order.RestaurantId, order.CustomerId, ct), warnings);

        await Task.WhenAll(restaurantTask, ratingTask, reviewsTask);

        var restaurant = await restaurantTask;
        var rating = await ratingTask;
        var customerReviews = await reviewsTask ?? [];

        // 3) узгодженість: беремо лише страви, що є в ЦЬОМУ замовленні, і відгуки саме на них / на цей ресторан
        var orderDishIds = order.Lines.Select(l => l.DishId).ToHashSet();
        var dishes = restaurant?.Dishes.Where(d => orderDishIds.Contains(d.Id)).ToDictionary(d => d.Id)
                     ?? new Dictionary<long, MenuDishDto>();
        var relevantReviews = customerReviews
            .Where(r => r.RestaurantId == order.RestaurantId && (r.DishId is null || orderDishIds.Contains(r.DishId.Value)))
            .ToList();

        if (restaurant is not null && restaurant.Id != order.RestaurantId)
            lock (warnings) warnings.Add($"{MenuClient.ServiceName} returned restaurant {restaurant.Id} instead of {order.RestaurantId}");

        var items = order.Lines.Select(line =>
        {
            dishes.TryGetValue(line.DishId, out var dish);
            var dishRatings = relevantReviews.Where(r => r.DishId == line.DishId).Select(r => r.Rating).ToList();
            return new OrderItemCardDto(
                line.DishId, line.DishName, line.Quantity, line.UnitPrice,
                CurrentPrice: dish?.Price,
                AvailableNow: dish?.IsAvailable,
                DishRating: dishRatings.Count > 0 ? dishRatings.Average() : null);
        }).ToList();

        logger.LogInformation(
            "Aggregated order {OrderId}: {ItemsCount} items, {ReviewsCount} reviews, {WarningsCount} warnings",
            orderId, items.Count, relevantReviews.Count, warnings.Count);
        activity?.SetTag("aggregator.warnings", warnings.Count);

        return new FullOrderDto(
            new OrderCardDto(order.Id, order.OrderNumber, order.CustomerId, order.Status, order.PlacedAt,
                order.ItemsTotal, order.DeliveryFee, order.TotalAmount),
            restaurant is null && rating is null ? null : ToRestaurantCard(order.RestaurantId, order.RestaurantName, restaurant, rating),
            items,
            relevantReviews.Select(ToReviewCard).ToList(),
            warnings);
    }

    /// <summary>
    /// Картка ресторану: меню (menu-api) + рейтинг + останні 3 відгуки (reviews-api) — усі три виклики паралельно.
    /// null — ресторан не знайдено.
    /// </summary>
    public async Task<RestaurantOverviewDto?> GetRestaurantOverviewAsync(long restaurantId, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("AggregateRestaurantOverview");
        activity?.SetTag("restaurant.id", restaurantId);

        var warnings = new List<string>();

        var restaurantTask = SafeAsync(MenuClient.ServiceName, $"restaurant {restaurantId}",
            () => menu.GetRestaurantAsync(restaurantId, onlyAvailable: true, ct), warnings);
        var ratingTask = SafeAsync(ReviewsClient.ServiceName, $"rating of restaurant {restaurantId}",
            () => reviews.GetRestaurantRatingAsync(restaurantId, ct), warnings);
        var latestTask = SafeAsync(ReviewsClient.ServiceName, $"latest reviews of restaurant {restaurantId}",
            () => reviews.GetLatestForRestaurantAsync(restaurantId, LatestReviewsCount, ct), warnings);

        await Task.WhenAll(restaurantTask, ratingTask, latestTask);

        var restaurant = await restaurantTask;
        var rating = await ratingTask;
        var latest = await latestTask ?? [];

        if (restaurant is null)
        {
            // menu-api живий, але ресторану немає → 404; menu-api недоступний → 503 (без нього картки не буде)
            if (warnings.Any(w => w.StartsWith(MenuClient.ServiceName)))
                throw new DependencyUnavailableException(MenuClient.ServiceName);
            return null;
        }

        logger.LogInformation("Aggregated restaurant {RestaurantId}: {DishesCount} dishes, {ReviewsCount} reviews, {WarningsCount} warnings",
            restaurantId, restaurant.Dishes.Count, latest.Count, warnings.Count);

        return new RestaurantOverviewDto(
            ToRestaurantCard(restaurantId, restaurant.Name, restaurant, rating),
            restaurant.Dishes,
            latest.Where(r => r.RestaurantId == restaurantId).Select(ToReviewCard).ToList(),
            warnings);
    }

    // ------------------------------------------------------------------ виклики з логуванням і частковими відмовами

    /// <summary>
    /// Виклик сервісу з логуванням тривалості. Недоступність (HttpRequestException, таймаут, circuit breaker)
    /// НЕ валить запит: повертається default, а в warnings додається повідомлення.
    /// Скасування клієнтом (ct) не ковтається — пробрасується далі.
    /// </summary>
    private async Task<T?> SafeAsync<T>(string service, string what, Func<Task<T?>> call, List<string> warnings)
    {
        logger.LogInformation("Calling {Service} for {What}", service, what);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await call();
            logger.LogInformation("{Service} responded in {ElapsedMs} ms for {What}", service, sw.ElapsedMilliseconds, what);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or TimeoutException
                                       or System.Text.Json.JsonException
                                       or Polly.ExecutionRejectedException   // таймаут / розімкнений circuit breaker
                                       or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            logger.LogWarning(ex, "{Service} is unavailable for {What} after {ElapsedMs} ms", service, what, sw.ElapsedMilliseconds);
            lock (warnings) warnings.Add($"{service} is unavailable: {what} not loaded");
            return default;
        }
    }

    // ------------------------------------------------------------------ мапінг у бізнес-DTO

    private static RestaurantCardDto ToRestaurantCard(long id, string fallbackName, MenuRestaurantDto? restaurant, ReviewsRatingDto? rating)
        => new(
            id,
            restaurant?.Name ?? fallbackName,
            restaurant?.City ?? "",
            restaurant?.CuisineType ?? "",
            restaurant?.IsActive ?? true,
            restaurant?.Profile?.Address,
            restaurant?.Profile?.Phone,
            restaurant?.Profile is { } p ? $"{p.OpensAt:HH\\:mm}–{p.ClosesAt:HH\\:mm}" : null,
            rating is { ReviewsCount: > 0 } ? Math.Round(rating.AverageRating, 2) : null,
            rating?.ReviewsCount);

    private static ReviewCardDto ToReviewCard(ReviewItemDto r)
        => new(r.Id, r.TargetType, r.DishName, r.AuthorName, r.Rating, r.Text, r.CreatedAt);
}

/// <summary>Обов'язковий для відповіді сервіс недоступний → 503 ProblemDetails.</summary>
public sealed class DependencyUnavailableException(string service)
    : Exception($"{service} is unavailable")
{
    public string Service { get; } = service;
}
