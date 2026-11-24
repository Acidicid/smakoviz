using Menu.Bll.Dtos;
using Menu.Bll.Services;

namespace Menu.Bll.Caching;

// Декоратори (патерн Decorator) над сервісами з Лаб. 03: самі сервіси НЕ змінено.
// Читання → cache-aside через ITwoLevelCache (L1 пам'ять → L2 Redis → БД).
// Запис → спершу операція в БД, потім ОДРАЗУ інвалідація пов'язаних ключів (картка + списки, куди входить запис).
// Списки з довільними фільтрами/пагінацією (GetListAsync) свідомо не кешуються: комбінацій ключів забагато,
// а інвалідувати їх точково неможливо — вони читаються з БД з AsNoTracking і пагінацією в SQL.
// Реєстрація в Program.cs: IDishService → CachedDishService(DishService) тощо. REST-контролери і gRPC-сервіс
// працюють через ті самі інтерфейси, тож кеш спільний для обох протоколів без дублювання коду.

public sealed class CachedDishService(DishService inner, ITwoLevelCache cache) : IDishService
{
    public Task<PagedResult<DishListItemDto>> GetListAsync(DishQuery query, CancellationToken ct = default)
        => inner.GetListAsync(query, ct);

    public async Task<DishDto> GetByIdAsync(long id, CancellationToken ct = default)
        => (await cache.GetOrCreateAsync<DishDto>(MenuCacheKeys.Dish(id),
               async token => await inner.GetByIdAsync(id, token), CachePolicy.Card, ct))!;

    public async Task<DishDto> CreateAsync(CreateDishDto input, CancellationToken ct = default)
    {
        var dish = await inner.CreateAsync(input, ct);
        await InvalidateAsync(dish, "DishCreated", ct);
        return dish;
    }

    public async Task<DishDto> ChangePriceAsync(long id, ChangePriceDto input, CancellationToken ct = default)
        => await InvalidateAsync(await inner.ChangePriceAsync(id, input, ct), "DishPriceChanged", ct);

    public async Task<DishDto> AddPortionsAsync(long id, int quantity, CancellationToken ct = default)
        => await InvalidateAsync(await inner.AddPortionsAsync(id, quantity, ct), "DishPortionsAdded", ct);

    public async Task<DishDto> RemovePortionsAsync(long id, int quantity, CancellationToken ct = default)
        => await InvalidateAsync(await inner.RemovePortionsAsync(id, quantity, ct), "DishPortionsRemoved", ct);

    public async Task<DishDto> SetAvailabilityAsync(long id, bool isAvailable, CancellationToken ct = default)
        => await InvalidateAsync(await inner.SetAvailabilityAsync(id, isAvailable, ct), "DishAvailabilityChanged", ct);

    public async Task<DishDto> AddToCategoryAsync(long id, AddDishToCategoryDto input, CancellationToken ct = default)
        => await InvalidateAsync(await inner.AddToCategoryAsync(id, input, ct), "DishCategoryAdded", ct);

    public async Task<DishDto> RemoveFromCategoryAsync(long id, int categoryId, CancellationToken ct = default)
        => await InvalidateAsync(await inner.RemoveFromCategoryAsync(id, categoryId, ct), "DishCategoryRemoved", ct);

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var dish = await inner.GetByIdAsync(id, ct);   // потрібен RestaurantId, щоб скинути картку ресторану
        await inner.DeleteAsync(id, ct);
        await InvalidateAsync(dish, "DishDeleted", ct);
    }

    /// <summary>Страва змінилась → її картка, обидві картки ресторану (меню) і список категорій (лічильник доступних страв).</summary>
    private async Task<DishDto> InvalidateAsync(DishDto dish, string reason, CancellationToken ct)
    {
        await cache.InvalidateAsync(reason, ct,
            [MenuCacheKeys.Dish(dish.Id), .. MenuCacheKeys.RestaurantAll(dish.RestaurantId), MenuCacheKeys.CategoriesAll]);
        return dish;
    }
}

public sealed class CachedRestaurantService(RestaurantService inner, ITwoLevelCache cache) : IRestaurantService
{
    public Task<PagedResult<RestaurantDto>> GetListAsync(RestaurantQuery query, CancellationToken ct = default)
        => inner.GetListAsync(query, ct);

    public async Task<RestaurantDetailsDto> GetByIdAsync(long id, bool onlyAvailableDishes, CancellationToken ct = default)
        => (await cache.GetOrCreateAsync<RestaurantDetailsDto>(MenuCacheKeys.Restaurant(id, onlyAvailableDishes),
               async token => await inner.GetByIdAsync(id, onlyAvailableDishes, token), CachePolicy.Card, ct))!;

    public Task<RestaurantDetailsDto> CreateAsync(CreateRestaurantDto input, CancellationToken ct = default)
        => inner.CreateAsync(input, ct);   // новий id — у кеші його ще немає

    public async Task<RestaurantDetailsDto> SetProfileAsync(long id, RestaurantProfileInputDto input, CancellationToken ct = default)
    {
        var restaurant = await inner.SetProfileAsync(id, input, ct);
        await cache.InvalidateAsync("RestaurantProfileChanged", ct, MenuCacheKeys.RestaurantAll(id));
        return restaurant;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await inner.DeleteAsync(id, ct);
        await cache.InvalidateAsync("RestaurantDeleted", ct, MenuCacheKeys.RestaurantAll(id));
    }
}

public sealed class CachedCategoryService(CategoryService inner, ITwoLevelCache cache) : ICategoryService
{
    /// <summary>Довідник категорій — найчастіше читаний і майже незмінний: політика Reference.</summary>
    public async Task<IReadOnlyList<CategoryWithStatsDto>> GetAllAsync(CancellationToken ct = default)
        => (await cache.GetOrCreateAsync<List<CategoryWithStatsDto>>(MenuCacheKeys.CategoriesAll,
               async token => [.. await inner.GetAllAsync(token)], CachePolicy.Reference, ct))!;

    public async Task<CategoryDto> GetByIdAsync(int id, CancellationToken ct = default)
        => (await cache.GetOrCreateAsync<CategoryDto>(MenuCacheKeys.Category(id),
               async token => await inner.GetByIdAsync(id, token), CachePolicy.Reference, ct))!;

    public async Task<CategoryDto> CreateAsync(CreateCategoryDto input, CancellationToken ct = default)
    {
        var category = await inner.CreateAsync(input, ct);
        await cache.InvalidateAsync("CategoryCreated", ct, MenuCacheKeys.CategoriesAll);
        return category;
    }

    public Task<IReadOnlyList<DishListItemDto>> GetRecentlyAddedAsync(string slug, int days, CancellationToken ct = default)
        => inner.GetRecentlyAddedAsync(slug, days, ct);

    public Task<IReadOnlyList<DishListItemDto>> GetFeaturedAsync(int id, int take, CancellationToken ct = default)
        => inner.GetFeaturedAsync(id, take, ct);
}
