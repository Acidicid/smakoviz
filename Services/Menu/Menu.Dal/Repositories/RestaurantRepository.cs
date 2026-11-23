using Menu.Dal.Context;
using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Menu.Dal.Repositories;

/// <summary>Ресторани — демонстрація EXPLICIT LOADING.</summary>
public sealed class RestaurantRepository(MenuDbContext context)
    : GenericRepository<Restaurant>(context), IRestaurantRepository
{
    /// <summary>
    /// EXPLICIT LOADING: спершу лише ресторан (запит №1), потім — окремими запитами і лише тоді,
    /// коли вони справді потрібні, — профіль (запит №2) і меню (запит №3).
    /// Доречно, коли пов'язані дані потрібні не завжди або їх треба відфільтрувати
    /// (тут: лише доступні страви, впорядковані за назвою).
    /// </summary>
    public async Task<Restaurant?> GetWithMenuAsync(long id, bool onlyAvailable, CancellationToken ct = default)
    {
        var restaurant = await Set.FirstOrDefaultAsync(r => r.Id == id, ct);          // запит №1
        if (restaurant is null) return null;

        await Context.Entry(restaurant).Reference(r => r.Profile).LoadAsync(ct);         // запит №2

        var dishes = Context.Entry(restaurant).Collection(r => r.Dishes).Query();
        if (onlyAvailable)
            dishes = dishes.Where(d => d.IsAvailable);
        await dishes
            .Include(d => d.DishCategories)
            .OrderBy(d => d.Name)
            .LoadAsync(ct);                                                              // запит №3

        return restaurant;
    }

    /// <summary>Ресторан + профіль (для редагування профілю).</summary>
    public async Task<Restaurant?> GetWithProfileAsync(long id, CancellationToken ct = default)
    {
        var restaurant = await Set.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (restaurant is not null)
            await Context.Entry(restaurant).Reference(r => r.Profile).LoadAsync(ct);
        return restaurant;
    }

    public Task<bool> ExistsBySlugAsync(string slug, long? exceptRestaurantId = null, CancellationToken ct = default)
        // IgnoreQueryFilters: slug унікальний серед УСІХ рядків, у т.ч. м'яко видалених (так діє UNIQUE-індекс)
        => Set.IgnoreQueryFilters()
            .AnyAsync(r => r.Slug == slug && (exceptRestaurantId == null || r.Id != exceptRestaurantId), ct);

    public Task<bool> HasDishesAsync(long restaurantId, CancellationToken ct = default)
        => Context.Dishes.AnyAsync(d => d.RestaurantId == restaurantId, ct);
}
