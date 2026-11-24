using System.Diagnostics;
using Delivery.Api.IntegrationEvents;
using Delivery.Bll.Caching;
using Delivery.Bll.Dtos;
using Delivery.Bll.Services;
using Delivery.Domain.Models;

namespace Delivery.Api.Metrics;

/// <summary>
/// Декоратор метрик над IOrderService (ланцюжок: InstrumentedOrderService → PublishingOrderService (Лаб. 08) → CachedOrderService → OrderService).
/// Тривалість записується для КОЖНОГО виконання — і успішного, і невдалого (інакше картина викривлена);
/// лічильники збільшуються одразу ПІСЛЯ успішної операції. Працює і для REST, і для gRPC.
/// </summary>
public sealed class InstrumentedOrderService(PublishingOrderService inner, DeliveryMetrics metrics) : IOrderService
{
    public Task<OrderDto> GetByIdAsync(long id, CancellationToken ct = default)
        => Measure("get", () => inner.GetByIdAsync(id, ct));

    public Task<PagedResult<OrderSummaryDto>> SearchAsync(long? customerId, string? status, int page, int pageSize,
        CancellationToken ct = default)
        => Measure("search", () => inner.SearchAsync(customerId, status, page, pageSize, ct));

    public async Task<PlaceOrderResult> PlaceOrderAsync(PlaceOrderDto input, string? idempotencyKey, CancellationToken ct = default)
    {
        var result = await Measure("place", () => inner.PlaceOrderAsync(input, idempotencyKey, ct));
        metrics.OrderCreated(result.Order.Status, result.Order.TotalAmount, result.Order.Lines.Count, replayed: !result.Created);
        return result;
    }

    public Task<OrderDto> AddLineAsync(long orderId, AddOrderLineDto input, CancellationToken ct = default)
        => Measure("add_line", () => inner.AddLineAsync(orderId, input, ct));

    public async Task ConfirmAsync(long orderId, CancellationToken ct = default)
    {
        await Measure("confirm", () => inner.ConfirmAsync(orderId, ct));
        metrics.OrderConfirmed();
        metrics.StatusChanged(OrderStatus.Confirmed);
    }

    public async Task CancelAsync(long orderId, string? reason, CancellationToken ct = default)
    {
        await Measure("cancel", () => inner.CancelAsync(orderId, reason, ct));
        metrics.OrderCancelled();
        metrics.StatusChanged(OrderStatus.Cancelled);
    }

    public async Task ChangeStatusAsync(long orderId, ChangeOrderStatusDto input, CancellationToken ct = default)
    {
        await Measure("change_status", () => inner.ChangeStatusAsync(orderId, input, ct));
        metrics.StatusChanged(input.Status);
    }

    public Task DeleteAsync(long orderId, CancellationToken ct = default)
        => Measure("delete", () => inner.DeleteAsync(orderId, ct));

    private async Task<T> Measure<T>(string operation, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            metrics.RecordDuration(operation, success: true, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch
        {
            metrics.RecordDuration(operation, success: false, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    private Task Measure(string operation, Func<Task> action)
        => Measure<bool>(operation, async () => { await action(); return true; });
}

/// <summary>
/// Раз на 30 с рахує активні замовлення за статусами (COUNT з пагінацією, pageSize = 1) для gauge orders.active.
/// Збій БД лише логується — метрика просто не оновиться.
/// </summary>
public sealed class OrdersSnapshotRefresher(IServiceScopeFactory scopeFactory, OrdersSnapshot snapshot,
    ILogger<OrdersSnapshotRefresher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();   // напряму, без кешу й метрик
                var values = new Dictionary<string, long>();
                foreach (var status in OrdersSnapshot.ActiveStatuses)
                    values[status] = (await orders.SearchAsync(null, status, 1, 1, stoppingToken)).TotalCount;
                snapshot.Update(values);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Orders snapshot refresh failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
