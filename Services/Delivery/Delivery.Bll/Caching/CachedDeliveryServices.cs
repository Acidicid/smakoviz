using Delivery.Bll.Dtos;
using Delivery.Bll.Services;
using Delivery.Domain.Models;

namespace Delivery.Bll.Caching;

// Декоратори над сервісами з Лаб. 02 (самі сервіси не змінено).
// • Копія страв Меню — read-heavy, змінюється лише синхронізацією (PUT) → політика Card.
// • Картка замовлення — її часто читає Aggregator, але вона містить адресу/телефон і змінює статус →
//   політика Volatile (L1 15 с, L2 60 с) + інвалідація після КОЖНОЇ зміни статусу чи позицій.
// • Клієнти (персональні дані) і пошук замовлень з фільтрами — не кешуються.

public sealed class CachedOrderService(OrderService inner, ITwoLevelCache cache) : IOrderService
{
    public async Task<OrderDto> GetByIdAsync(long id, CancellationToken ct = default)
        => (await cache.GetOrCreateAsync<OrderDto>(DeliveryCacheKeys.Order(id),
               async token => await inner.GetByIdAsync(id, token), CachePolicy.Volatile, ct))!;

    public Task<PagedResult<OrderSummaryDto>> SearchAsync(long? customerId, string? status, int page, int pageSize,
        CancellationToken ct = default) => inner.SearchAsync(customerId, status, page, pageSize, ct);

    public Task<PlaceOrderResult> PlaceOrderAsync(PlaceOrderDto input, string? idempotencyKey, CancellationToken ct = default)
        => inner.PlaceOrderAsync(input, idempotencyKey, ct);   // нове замовлення — у кеші його ще немає

    public async Task<OrderDto> AddLineAsync(long orderId, AddOrderLineDto input, CancellationToken ct = default)
    {
        var order = await inner.AddLineAsync(orderId, input, ct);
        await cache.InvalidateAsync("OrderLineAdded", ct, DeliveryCacheKeys.Order(orderId));
        return order;
    }

    public async Task ConfirmAsync(long orderId, CancellationToken ct = default)
    {
        await inner.ConfirmAsync(orderId, ct);
        await cache.InvalidateAsync("OrderConfirmed", ct, DeliveryCacheKeys.Order(orderId));
    }

    public async Task CancelAsync(long orderId, string? reason, CancellationToken ct = default)
    {
        await inner.CancelAsync(orderId, reason, ct);
        await cache.InvalidateAsync("OrderCancelled", ct, DeliveryCacheKeys.Order(orderId));
    }

    public async Task ChangeStatusAsync(long orderId, ChangeOrderStatusDto input, CancellationToken ct = default)
    {
        await inner.ChangeStatusAsync(orderId, input, ct);
        await cache.InvalidateAsync("OrderStatusChanged", ct, DeliveryCacheKeys.Order(orderId));
    }

    public async Task DeleteAsync(long orderId, CancellationToken ct = default)
    {
        await inner.DeleteAsync(orderId, ct);
        await cache.InvalidateAsync("OrderDeleted", ct, DeliveryCacheKeys.Order(orderId));
    }
}

public sealed class CachedDishService(DishService inner, ITwoLevelCache cache) : IDishService
{
    public async Task<DishDto> GetByIdAsync(long id, CancellationToken ct = default)
        => (await cache.GetOrCreateAsync<DishDto>(DeliveryCacheKeys.Dish(id),
               async token => await inner.GetByIdAsync(id, token), CachePolicy.Card, ct))!;

    public async Task<IReadOnlyList<DishDto>> GetByRestaurantAsync(long restaurantId, bool onlyAvailable, CancellationToken ct = default)
        => (await cache.GetOrCreateAsync<List<DishDto>>(DeliveryCacheKeys.RestaurantDishes(restaurantId, onlyAvailable),
               async token => [.. await inner.GetByRestaurantAsync(restaurantId, onlyAvailable, token)], CachePolicy.Card, ct))!;

    public async Task<DishDto> UpsertAsync(long id, UpsertDishDto input, CancellationToken ct = default)
    {
        var dish = await inner.UpsertAsync(id, input, ct);
        await cache.InvalidateAsync("DishSynchronized", ct,
            [DeliveryCacheKeys.Dish(id), .. DeliveryCacheKeys.RestaurantDishesAll(dish.RestaurantId)]);
        return dish;
    }
}
