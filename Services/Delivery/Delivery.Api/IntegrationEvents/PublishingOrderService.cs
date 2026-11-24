using Delivery.Bll.Caching;
using Delivery.Bll.Dtos;
using Delivery.Bll.Services;
using Delivery.Domain.Models;
using Platform.EventBus;

namespace Delivery.Api.IntegrationEvents;

/// <summary>
/// Публікація OrderPlaced (Лаб. 08). Ланцюжок: InstrumentedOrderService → PublishingOrderService → CachedOrderService → OrderService.
/// Подія публікується ЛИШЕ після того, як inner.PlaceOrderAsync закомітив транзакцію (3 репозиторії, Лаб. 02):
/// збій БД → виняток → події немає. Повтор з тим самим Idempotency-Key (Created = false) подію НЕ дублює.
/// </summary>
public sealed class PublishingOrderService(
    CachedOrderService inner,
    IEventPublisher publisher,
    IHttpContextAccessor http,
    ILogger<PublishingOrderService> logger) : IOrderService
{
    public async Task<PlaceOrderResult> PlaceOrderAsync(PlaceOrderDto input, string? idempotencyKey, CancellationToken ct = default)
    {
        var result = await inner.PlaceOrderAsync(input, idempotencyKey, ct);   // COMMIT уже відбувся
        if (!result.Created)
            return result;

        var order = result.Order;
        var integrationEvent = new OrderPlacedIntegrationEvent(order.Id, order.OrderNumber, order.RestaurantId, order.TotalAmount,
            order.Lines.Select(l => new OrderPlacedLine(l.DishId, l.Quantity)).ToList())
        {
            CorrelationId = http.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString()
        };

        try
        {
            await publisher.PublishAsync(integrationEvent, DeliveryRoutingKeys.OrderPlaced, ct);
        }
        catch (Exception ex)
        {
            // замовлення вже збережено — клієнт отримує 201; розбіжність (порції не списані) видно в логах
            logger.LogError(ex, "Order {OrderId} saved but {EventType} {EventId} was not published",
                order.Id, nameof(OrderPlacedIntegrationEvent), integrationEvent.EventId);
        }

        return result;
    }

    public Task<OrderDto> GetByIdAsync(long id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

    public Task<PagedResult<OrderSummaryDto>> SearchAsync(long? customerId, string? status, int page, int pageSize,
        CancellationToken ct = default) => inner.SearchAsync(customerId, status, page, pageSize, ct);

    public Task<OrderDto> AddLineAsync(long orderId, AddOrderLineDto input, CancellationToken ct = default) => inner.AddLineAsync(orderId, input, ct);
    public Task ConfirmAsync(long orderId, CancellationToken ct = default) => inner.ConfirmAsync(orderId, ct);
    public Task CancelAsync(long orderId, string? reason, CancellationToken ct = default) => inner.CancelAsync(orderId, reason, ct);
    public Task ChangeStatusAsync(long orderId, ChangeOrderStatusDto input, CancellationToken ct = default) => inner.ChangeStatusAsync(orderId, input, ct);
    public Task DeleteAsync(long orderId, CancellationToken ct = default) => inner.DeleteAsync(orderId, ct);
}
