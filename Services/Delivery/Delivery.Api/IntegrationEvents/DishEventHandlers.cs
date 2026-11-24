using Delivery.Bll.Caching;
using Delivery.Dal.Abstractions;
using Delivery.Domain.Models;
using Platform.EventBus;

namespace Delivery.Api.IntegrationEvents;

/// <summary>
/// Реплікація за політикою дублювання з Лаб. 01: «Доставка» тримає КОПІЮ страв (dbo.Dishes) для перевірки
/// при оформленні. Оновлюється лише довідник — ціни в уже оформлених позиціях (OrderLines.UnitPrice) НЕ змінюються:
/// клієнт платить ту ціну, яку бачив у момент замовлення.
/// Ідемпотентність: EventId → dbo.ProcessedEvents у тій самій транзакції Unit of Work, що й MERGE страви.
/// </summary>
public sealed class DishUpdatedIntegrationEventHandler(
    IUnitOfWork uow,
    ITwoLevelCache cache,
    ILogger<DishUpdatedIntegrationEventHandler> logger) : IIntegrationEventHandler<DishUpdatedIntegrationEvent>
{
    public async Task HandleAsync(DishUpdatedIntegrationEvent e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Name) || e.Price <= 0 || e.DishId <= 0)
            throw new PermanentEventException($"Invalid dish data in event {e.EventId}");   // повтор не допоможе → DLQ

        var applied = await DishReplica.ApplyAsync(uow, e.EventId, nameof(DishUpdatedIntegrationEvent),
            new Dish { Id = e.DishId, RestaurantId = e.RestaurantId, Name = e.Name.Trim(), Price = e.Price, IsAvailable = e.IsAvailable }, ct);

        if (!applied)
        {
            logger.LogInformation("Event {EventId} already processed — dish {DishId} skipped", e.EventId, e.DishId);
            return;
        }

        await cache.InvalidateAsync("DishUpdatedEvent", ct,
            [DeliveryCacheKeys.Dish(e.DishId), .. DeliveryCacheKeys.RestaurantDishesAll(e.RestaurantId)]);
        logger.LogInformation("Dish {DishId} of restaurant {RestaurantId} replicated: price {Price}, available {IsAvailable}",
            e.DishId, e.RestaurantId, e.Price, e.IsAvailable);
    }
}

/// <summary>Страву видалено в «Меню» → у копії вона стає недоступною (історичні замовлення на неї посилаються).</summary>
public sealed class DishDeletedIntegrationEventHandler(
    IUnitOfWork uow,
    ITwoLevelCache cache,
    ILogger<DishDeletedIntegrationEventHandler> logger) : IIntegrationEventHandler<DishDeletedIntegrationEvent>
{
    public async Task HandleAsync(DishDeletedIntegrationEvent e, CancellationToken ct)
    {
        var existing = await uow.Dishes.GetByIdAsync(e.DishId, ct);
        if (existing is null)
        {
            logger.LogInformation("Dish {DishId} is not in the local copy — nothing to disable", e.DishId);
            return;
        }

        existing.IsAvailable = false;
        if (!await DishReplica.ApplyAsync(uow, e.EventId, nameof(DishDeletedIntegrationEvent), existing, ct))
            return;

        await cache.InvalidateAsync("DishDeletedEvent", ct,
            [DeliveryCacheKeys.Dish(e.DishId), .. DeliveryCacheKeys.RestaurantDishesAll(e.RestaurantId)]);
        logger.LogInformation("Dish {DishId} disabled in the local copy after deletion in Menu", e.DishId);
    }
}

internal static class DishReplica
{
    private const string Consumer = "delivery-api";

    /// <summary>Одна транзакція: позначка «оброблено» + MERGE страви. false — подію вже оброблено.</summary>
    public static async Task<bool> ApplyAsync(IUnitOfWork uow, Guid eventId, string eventType, Dish dish, CancellationToken ct)
    {
        if (await uow.ProcessedEvents.IsProcessedAsync(eventId, ct))
            return false;

        await uow.BeginTransactionAsync(ct: ct);
        try
        {
            if (!await uow.ProcessedEvents.TryMarkProcessedAsync(eventId, eventType, Consumer, ct))
            {
                await uow.RollbackAsync(ct);
                return false;
            }

            await uow.Dishes.UpsertAsync(dish, ct);
            await uow.CommitAsync(ct);
            return true;
        }
        catch
        {
            if (uow.HasActiveTransaction) await uow.RollbackAsync(ct);
            throw;
        }
    }
}
