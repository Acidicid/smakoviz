using Menu.Dal.Context;
using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Menu.Dal.Repositories;

/// <summary>
/// LINQ to Entities для «багато до багатьох»: запити йдуть ЧЕРЕЗ проміжну сутність DishCategory
/// і використовують її ВЛАСНІ поля (AddedAt, IsFeatured) — те, що неможливо з автоматичним M:N.
/// </summary>
public sealed class DishCategoryRepository(MenuDbContext context)
    : GenericRepository<DishCategory>(context), IDishCategoryRepository
{
    public async Task<IReadOnlyList<Dish>> GetRecentlyAddedAsync(string categorySlug, DateTime since,
        CancellationToken ct = default)
        => await Set
            .Where(dc => dc.Category.Slug == categorySlug && dc.AddedAt >= since)
            .OrderByDescending(dc => dc.AddedAt)
            .Select(dc => dc.Dish)
            .Include(d => d.Restaurant)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Dish>> GetFeaturedAsync(int categoryId, int take, CancellationToken ct = default)
        => await Set
            .Where(dc => dc.CategoryId == categoryId && dc.IsFeatured && dc.Dish.IsAvailable)
            .OrderByDescending(dc => dc.AddedAt)
            .Select(dc => dc.Dish)
            .Include(d => d.Restaurant)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<int, int>> CountAvailableDishesByCategoryAsync(CancellationToken ct = default)
        => await Set
            .Where(dc => dc.Dish.IsAvailable)
            .GroupBy(dc => dc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);
}
