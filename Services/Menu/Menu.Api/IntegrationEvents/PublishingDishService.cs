using Menu.Bll.Caching;
using Menu.Bll.Dtos;
using Menu.Bll.Services;
using Platform.EventBus;

namespace Menu.Api.IntegrationEvents;

/// <summary>
/// Публікація подій «Меню» (Лаб. 08). Ланцюжок: InstrumentedDishService → PublishingDishService → CachedDishService → DishService.
/// Подія йде ЛИШЕ ПІСЛЯ успішного SaveChanges усередині inner: якщо БД відмовила — виняток вилетить раніше,
/// і подія не буде опублікована (інакше споживачі отримали б «зміну», якої не існує).
/// Збій самого брокера не відкочує вже збережену зміну — він логується (Outbox — наступний крок, див. README).
/// </summary>
public sealed class PublishingDishService(
    CachedDishService inner,
    IEventPublisher publisher,
    IHttpContextAccessor http,
    ILogger<PublishingDishService> logger) : IDishService
{
    public Task<PagedResult<DishListItemDto>> GetListAsync(DishQuery query, CancellationToken ct = default) => inner.GetListAsync(query, ct);
    public Task<DishDto> GetByIdAsync(long id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

    public async Task<DishDto> CreateAsync(CreateDishDto input, CancellationToken ct = default)
        => await PublishUpdatedAsync(await inner.CreateAsync(input, ct), ct);

    public async Task<DishDto> ChangePriceAsync(long id, ChangePriceDto input, CancellationToken ct = default)
        => await PublishUpdatedAsync(await inner.ChangePriceAsync(id, input, ct), ct);

    public async Task<DishDto> AddPortionsAsync(long id, int quantity, CancellationToken ct = default)
        => await PublishUpdatedAsync(await inner.AddPortionsAsync(id, quantity, ct), ct);

    public async Task<DishDto> RemovePortionsAsync(long id, int quantity, CancellationToken ct = default)
        => await PublishUpdatedAsync(await inner.RemovePortionsAsync(id, quantity, ct), ct);

    public async Task<DishDto> SetAvailabilityAsync(long id, bool isAvailable, CancellationToken ct = default)
        => await PublishUpdatedAsync(await inner.SetAvailabilityAsync(id, isAvailable, ct), ct);

    public Task<DishDto> AddToCategoryAsync(long id, AddDishToCategoryDto input, CancellationToken ct = default)
        => inner.AddToCategoryAsync(id, input, ct);   // категорії не реплікуються — подія не потрібна

    public Task<DishDto> RemoveFromCategoryAsync(long id, int categoryId, CancellationToken ct = default)
        => inner.RemoveFromCategoryAsync(id, categoryId, ct);

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var dish = await inner.GetByIdAsync(id, ct);
        await inner.DeleteAsync(id, ct);                      // SaveChanges успішний →
        await SafePublishAsync(new DishDeletedIntegrationEvent(dish.Id, dish.RestaurantId) { CorrelationId = CorrelationId },
            MenuRoutingKeys.DishDeleted, ct);                  // → лише тепер подія
    }

    private async Task<DishDto> PublishUpdatedAsync(DishDto dish, CancellationToken ct)
    {
        await SafePublishAsync(new DishUpdatedIntegrationEvent(dish.Id, dish.RestaurantId, dish.RestaurantName, dish.Name,
            dish.Price, dish.IsAvailable, dish.AvailablePortions) { CorrelationId = CorrelationId }, MenuRoutingKeys.DishUpdated, ct);
        return dish;
    }

    private string? CorrelationId => http.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();

    private async Task SafePublishAsync<TEvent>(TEvent integrationEvent, string routingKey, CancellationToken ct)
        where TEvent : IntegrationEvent
    {
        try
        {
            await publisher.PublishAsync(integrationEvent, routingKey, ct);
        }
        catch (Exception ex)
        {
            // зміну в БД уже збережено — клієнт отримує успіх, а розбіжність копій видно в логах (Error)
            logger.LogError(ex, "Dish change saved but {EventType} {EventId} was not published", typeof(TEvent).Name, integrationEvent.EventId);
        }
    }
}
