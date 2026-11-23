using Menu.Dal.Context;
using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Menu.Dal.Repositories;

/// <summary>Страви — демонстрація EAGER LOADING.</summary>
public sealed class DishRepository(MenuDbContext context) : GenericRepository<Dish>(context), IDishRepository
{
    /// <summary>
    /// EAGER LOADING: страва + ресторан + профіль ресторану + категорії ОДНИМ SQL-запитом (JOIN-и).
    /// У лозі EF Core (Microsoft.EntityFrameworkCore.Database.Command) — рівно один SELECT.
    /// Доречно, коли пов'язані дані потрібні ЗАВЖДИ (картка страви).
    /// Сутність відстежується (tracking), бо сервіс далі може її змінювати.
    /// </summary>
    public Task<Dish?> GetWithDetailsAsync(long id, CancellationToken ct = default)
        => Set
            .Include(d => d.Restaurant)
                .ThenInclude(r => r.Profile)
            .Include(d => d.DishCategories)
                .ThenInclude(dc => dc.Category)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<bool> ExistsByNameAsync(long restaurantId, string name, long? exceptDishId = null,
        CancellationToken ct = default)
        => Set.AnyAsync(d => d.RestaurantId == restaurantId
                             && d.Name == name
                             && (exceptDishId == null || d.Id != exceptDishId), ct);

    public void SetExpectedRowVersion(Dish dish, byte[] rowVersion)
        // EF підставить ОРИГІНАЛЬНЕ значення у WHERE RowVersion = @p; якщо рядок уже змінився — 0 рядків → конфлікт
        => Context.Entry(dish).Property(d => d.RowVersion).OriginalValue = rowVersion;
}
