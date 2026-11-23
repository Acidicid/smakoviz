using Menu.Bll.Dtos;

namespace Menu.Bll.Services;

public interface IRestaurantService
{
    Task<PagedResult<RestaurantDto>> GetListAsync(RestaurantQuery query, CancellationToken ct = default);
    Task<RestaurantDetailsDto> GetByIdAsync(long id, bool onlyAvailableDishes, CancellationToken ct = default);
    Task<RestaurantDetailsDto> CreateAsync(CreateRestaurantDto input, CancellationToken ct = default);
    Task<RestaurantDetailsDto> SetProfileAsync(long id, RestaurantProfileInputDto input, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IDishService
{
    Task<PagedResult<DishListItemDto>> GetListAsync(DishQuery query, CancellationToken ct = default);
    Task<DishDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<DishDto> CreateAsync(CreateDishDto input, CancellationToken ct = default);
    Task<DishDto> ChangePriceAsync(long id, ChangePriceDto input, CancellationToken ct = default);
    Task<DishDto> AddPortionsAsync(long id, int quantity, CancellationToken ct = default);
    Task<DishDto> RemovePortionsAsync(long id, int quantity, CancellationToken ct = default);
    Task<DishDto> SetAvailabilityAsync(long id, bool isAvailable, CancellationToken ct = default);
    Task<DishDto> AddToCategoryAsync(long id, AddDishToCategoryDto input, CancellationToken ct = default);
    Task<DishDto> RemoveFromCategoryAsync(long id, int categoryId, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryWithStatsDto>> GetAllAsync(CancellationToken ct = default);
    Task<CategoryDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CategoryDto> CreateAsync(CreateCategoryDto input, CancellationToken ct = default);

    /// <summary>Нові страви категорії за останні N днів (M:N через AddedAt).</summary>
    Task<IReadOnlyList<DishListItemDto>> GetRecentlyAddedAsync(string slug, int days, CancellationToken ct = default);

    /// <summary>Рекомендовані страви категорії (M:N через IsFeatured).</summary>
    Task<IReadOnlyList<DishListItemDto>> GetFeaturedAsync(int id, int take, CancellationToken ct = default);
}
