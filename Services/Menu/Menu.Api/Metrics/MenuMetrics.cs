using System.Diagnostics;
using System.Diagnostics.Metrics;
using Menu.Api.IntegrationEvents;
using Menu.Bll.Caching;
using Menu.Bll.Dtos;
using Menu.Bll.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Menu.Api.Metrics;

/// <summary>
/// Бізнес-метрики сервісу «Меню» (Лаб. 07). Singleton; Meter "Menu.Api" підключено в ServiceDefaults.
/// Теги з низькою кардинальністю:
///   price.direction — up | down;  operation — get | create | change_price | add_portions | remove_portions |
///   set_availability | delete;  status — success | failure;  dish.available — true | false.
/// Без DishId / RestaurantId у тегах.
/// </summary>
public sealed class MenuMetrics
{
    public const string MeterName = "Menu.Api";

    private readonly Counter<long> _dishesCreated;
    private readonly Counter<long> _priceChanged;
    private readonly Counter<long> _portionsSold;
    private readonly Counter<long> _availabilityChanged;
    private readonly Histogram<double> _operationDuration;

    public MenuMetrics(IMeterFactory meterFactory, IMemoryCache memoryCache)
    {
        var meter = meterFactory.Create(MeterName);

        _dishesCreated = meter.CreateCounter<long>("dishes.created", unit: "{dish}", description: "Dishes added to menus");
        _priceChanged = meter.CreateCounter<long>("dishes.price_changed", unit: "{change}", description: "Dish price changes by direction");
        _portionsSold = meter.CreateCounter<long>("dishes.portions_removed", unit: "{portion}", description: "Portions written off (sold)");
        _availabilityChanged = meter.CreateCounter<long>("dishes.availability_changed", unit: "{change}", description: "Dish availability toggles");
        _operationDuration = meter.CreateHistogram<double>("dishes.operation.duration", unit: "ms",
            description: "Duration of dish operations (successful AND failed)");

        // Gauge: поточна кількість записів у кеші L1 (знімок у момент збору)
        meter.CreateObservableGauge("menu.cache.entries",
            () => (memoryCache as MemoryCache)?.GetCurrentStatistics()?.CurrentEntryCount ?? 0,
            unit: "{entry}", description: "Entries currently stored in the in-memory cache (L1)");
    }

    public void DishCreated(bool isAvailable)
        => _dishesCreated.Add(1, new KeyValuePair<string, object?>("dish.available", isAvailable));

    public void PriceChanged(decimal oldPrice, decimal newPrice)
        => _priceChanged.Add(1, new KeyValuePair<string, object?>("price.direction", newPrice >= oldPrice ? "up" : "down"));

    public void PortionsRemoved(int quantity) => _portionsSold.Add(quantity);

    public void AvailabilityChanged(bool isAvailable)
        => _availabilityChanged.Add(1, new KeyValuePair<string, object?>("dish.available", isAvailable));

    public void RecordDuration(string operation, bool success, double ms)
        => _operationDuration.Record(ms,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", success ? "success" : "failure"));
}

/// <summary>Декоратор метрик: InstrumentedDishService → PublishingDishService (Лаб. 08) → CachedDishService → DishService. Тривалість — для всіх виконань.</summary>
public sealed class InstrumentedDishService(PublishingDishService inner, MenuMetrics metrics) : IDishService
{
    public Task<PagedResult<DishListItemDto>> GetListAsync(DishQuery query, CancellationToken ct = default)
        => Measure("list", () => inner.GetListAsync(query, ct));

    public Task<DishDto> GetByIdAsync(long id, CancellationToken ct = default)
        => Measure("get", () => inner.GetByIdAsync(id, ct));

    public async Task<DishDto> CreateAsync(CreateDishDto input, CancellationToken ct = default)
    {
        var dish = await Measure("create", () => inner.CreateAsync(input, ct));
        metrics.DishCreated(dish.IsAvailable);
        return dish;
    }

    public async Task<DishDto> ChangePriceAsync(long id, ChangePriceDto input, CancellationToken ct = default)
    {
        var before = await inner.GetByIdAsync(id, ct);   // з кешу — дешево
        var dish = await Measure("change_price", () => inner.ChangePriceAsync(id, input, ct));
        metrics.PriceChanged(before.Price, dish.Price);
        return dish;
    }

    public Task<DishDto> AddPortionsAsync(long id, int quantity, CancellationToken ct = default)
        => Measure("add_portions", () => inner.AddPortionsAsync(id, quantity, ct));

    public async Task<DishDto> RemovePortionsAsync(long id, int quantity, CancellationToken ct = default)
    {
        var dish = await Measure("remove_portions", () => inner.RemovePortionsAsync(id, quantity, ct));
        metrics.PortionsRemoved(quantity);
        return dish;
    }

    public async Task<DishDto> SetAvailabilityAsync(long id, bool isAvailable, CancellationToken ct = default)
    {
        var dish = await Measure("set_availability", () => inner.SetAvailabilityAsync(id, isAvailable, ct));
        metrics.AvailabilityChanged(isAvailable);
        return dish;
    }

    public Task<DishDto> AddToCategoryAsync(long id, AddDishToCategoryDto input, CancellationToken ct = default)
        => Measure("add_category", () => inner.AddToCategoryAsync(id, input, ct));

    public Task<DishDto> RemoveFromCategoryAsync(long id, int categoryId, CancellationToken ct = default)
        => Measure("remove_category", () => inner.RemoveFromCategoryAsync(id, categoryId, ct));

    public Task DeleteAsync(long id, CancellationToken ct = default)
        => Measure("delete", async () => { await inner.DeleteAsync(id, ct); return true; });

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
}
