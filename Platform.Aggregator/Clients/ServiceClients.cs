using System.Net;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

// Typed HttpClients: по одному класу на мікросервіс. BaseAddress "https+http://<resource>" задається в Program.cs,
// реальну адресу підставляє service discovery, а resilience-handler і CorrelationIdHandler
// підключені до ВСІХ клієнтів у ServiceDefaults (ConfigureHttpClientDefaults).
// 404 → null (такого об'єкта немає — це не збій сервісу); інші помилки → виняток (його перетворить на Warning SafeAsync).

/// <summary>Клієнт delivery-api (замовлення).</summary>
public sealed class DeliveryClient(HttpClient http)
{
    public const string ServiceName = "delivery-api";

    public Task<DeliveryOrderDto?> GetOrderAsync(long orderId, CancellationToken ct)
        => http.GetOrNullAsync<DeliveryOrderDto>($"/api/delivery/orders/{orderId}", ct);
}

/// <summary>Клієнт menu-api (ресторани й страви — актуальні дані).</summary>
public sealed class MenuClient(HttpClient http)
{
    public const string ServiceName = "menu-api";

    /// <param name="onlyAvailable">false — показати і зняті з продажу страви (для картки замовлення).</param>
    public Task<MenuRestaurantDto?> GetRestaurantAsync(long restaurantId, bool onlyAvailable, CancellationToken ct)
        => http.GetOrNullAsync<MenuRestaurantDto>(
            $"/api/menu/restaurants/{restaurantId}?onlyAvailable={onlyAvailable.ToString().ToLowerInvariant()}", ct);
}

/// <summary>Клієнт reviews-api (рейтинг і відгуки).</summary>
public sealed class ReviewsClient(HttpClient http)
{
    public const string ServiceName = "reviews-api";

    public Task<ReviewsRatingDto?> GetRestaurantRatingAsync(long restaurantId, CancellationToken ct)
        => http.GetOrNullAsync<ReviewsRatingDto>($"/api/reviews/restaurants/{restaurantId}/rating", ct);

    public async Task<IReadOnlyList<ReviewItemDto>?> GetLatestForRestaurantAsync(long restaurantId, int count, CancellationToken ct)
    {
        var page = await http.GetOrNullAsync<ReviewsPageDto>(
            $"/api/reviews?restaurantId={restaurantId}&sortBy=createdAt&sortDir=desc&page=1&pageSize={count}", ct);
        return page?.Items ?? [];
    }

    public async Task<IReadOnlyList<ReviewItemDto>?> GetForCustomerAsync(long restaurantId, long customerId, CancellationToken ct)
    {
        var page = await http.GetOrNullAsync<ReviewsPageDto>(
            $"/api/reviews?restaurantId={restaurantId}&customerId={customerId}&page=1&pageSize=50", ct);
        return page?.Items ?? [];
    }
}

internal static class HttpClientJsonExtensions
{
    /// <summary>GET + десеріалізація (System.Text.Json, camelCase). 404 → null; 5xx/таймаут → HttpRequestException.</summary>
    public static async Task<T?> GetOrNullAsync<T>(this HttpClient http, string uri, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(uri, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}
