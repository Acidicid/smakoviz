using Delivery.Bll.Dtos;
using Delivery.Domain.Models;

namespace Delivery.Bll.Services;

public interface ICustomerService
{
    Task<CustomerDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<PagedResult<CustomerDto>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto input, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IDishService
{
    Task<DishDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<DishDto>> GetByRestaurantAsync(long restaurantId, bool onlyAvailable, CancellationToken ct = default);
    Task<DishDto> UpsertAsync(long id, UpsertDishDto input, CancellationToken ct = default);
}

public interface IOrderService
{
    Task<OrderDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<PagedResult<OrderSummaryDto>> SearchAsync(long? customerId, string? status, int page, int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Оформлення замовлення — транзакція на 3 репозиторії (Customers, Dishes, Orders).
    /// idempotencyKey: повторний запит з тим самим ключем поверне вже створене замовлення.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(PlaceOrderDto input, string? idempotencyKey, CancellationToken ct = default);

    Task<OrderDto> AddLineAsync(long orderId, AddOrderLineDto input, CancellationToken ct = default);
    Task ConfirmAsync(long orderId, CancellationToken ct = default);
    Task CancelAsync(long orderId, string? reason, CancellationToken ct = default);
    Task ChangeStatusAsync(long orderId, ChangeOrderStatusDto input, CancellationToken ct = default);
    Task DeleteAsync(long orderId, CancellationToken ct = default);
}

/// <summary>Created = false означає, що це повтор запиту з тим самим Idempotency-Key.</summary>
public sealed record PlaceOrderResult(OrderDto Order, bool Created);
