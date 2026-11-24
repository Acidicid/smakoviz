using Menu.Bll.Caching;
using Menu.Dal.Context;
using Menu.Dal.Messaging;
using Menu.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Platform.EventBus;

namespace Menu.Api.IntegrationEvents;

/// <summary>
/// «Доставка» оформила замовлення → «Меню» списує порції страв. Ідемпотентно: EventId записується в
/// dbo.ProcessedEvents у ТІЙ САМІЙ транзакції, що й зміна порцій. Повторна доставка → «вже оброблено» → Ack без змін.
/// Порцій не вистачає (доменне правило) — повтор не допоможе → PermanentEventException → DLQ.
/// </summary>
public sealed class OrderPlacedIntegrationEventHandler(
    MenuDbContext db,
    ITwoLevelCache cache,
    IEventPublisher publisher,
    ILogger<OrderPlacedIntegrationEventHandler> logger) : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    private const string Consumer = "menu-api";

    public async Task HandleAsync(OrderPlacedIntegrationEvent e, CancellationToken ct)
    {
        if (await ProcessedEventStore.IsProcessedAsync(db, e.EventId, ct))
        {
            logger.LogInformation("Event {EventId} already processed — skipping order {OrderId}", e.EventId, e.OrderId);
            return;
        }

        var dishIds = e.Lines.Select(l => l.DishId).Distinct().ToList();

        // EnableRetryOnFailure вимагає, щоб ручна транзакція виконувалась усередині execution strategy
        var strategy = db.Database.CreateExecutionStrategy();
        var applied = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if (!await ProcessedEventStore.TryMarkProcessedAsync(db, e.EventId, nameof(OrderPlacedIntegrationEvent), Consumer, ct))
            {
                await transaction.RollbackAsync(ct);
                return false;   // паралельний споживач уже обробив цю подію
            }

            var dishes = await db.Dishes.Where(d => dishIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
            try
            {
                foreach (var line in e.Lines)
                {
                    if (!dishes.TryGetValue(line.DishId, out var dish))
                        throw new PermanentEventException($"Dish {line.DishId} from order {e.OrderId} does not exist in the menu");
                    dish.RemovePortions(line.Quantity);   // доменне правило: не можна списати більше, ніж є
                }
            }
            catch (DomainException ex)
            {
                throw new PermanentEventException($"Cannot write off portions for order {e.OrderId}: {ex.Message}", ex);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);   // порції + ProcessedEvents — атомарно
            return true;
        });

        if (!applied)
        {
            logger.LogInformation("Event {EventId} was processed concurrently — skipping order {OrderId}", e.EventId, e.OrderId);
            return;
        }

        await cache.InvalidateAsync("OrderPlacedEvent", ct,
            [.. dishIds.Select(MenuCacheKeys.Dish), .. MenuCacheKeys.RestaurantAll(e.RestaurantId), MenuCacheKeys.CategoriesAll]);

        // залишки змінились → копії в інших сервісах оновлюються подією (після коміту!)
        foreach (var dish in await db.Dishes.AsNoTracking().Include(d => d.Restaurant)
                     .Where(d => dishIds.Contains(d.Id)).ToListAsync(ct))
        {
            await publisher.PublishAsync(new DishUpdatedIntegrationEvent(dish.Id, dish.RestaurantId, dish.Restaurant.Name,
                dish.Name, dish.Price, dish.IsAvailable, dish.AvailablePortions) { CorrelationId = e.CorrelationId },
                MenuRoutingKeys.DishUpdated, ct);
        }

        logger.LogInformation("Order {OrderId} ({OrderNumber}): written off portions for {DishesCount} dishes of restaurant {RestaurantId}",
            e.OrderId, e.OrderNumber, dishIds.Count, e.RestaurantId);
    }
}
