using Ardalis.Specification;
using Menu.Bll.Dtos;
using Menu.Domain.Entities;

namespace Menu.Bll.Specifications;

/// <summary>
/// Специфікація списку страв: ЩО вибрати (Where), що підвантажити (Include), як впорядкувати (OrderBy)
/// і яку сторінку взяти (Skip/Take). Не залежить від EF Core — це лише опис запиту,
/// тому її можна тестувати окремо. Репозиторій застосовує її через WithSpecification() і
/// виконує ОДИН SQL-запит: фільтри → WHERE, сортування → ORDER BY, сторінка → OFFSET ... FETCH.
/// </summary>
public sealed class DishesFilterSpec : Specification<Dish>
{
    /// <summary>Дозволені поля сортування (усе інше відхиляє DishQueryValidator → 400).</summary>
    public static readonly string[] SortFields = ["name", "price", "calories", "createdAt"];

    /// <param name="applyPaging">false — для підрахунку totalCount тими самими критеріями.</param>
    public DishesFilterSpec(DishQuery q, bool applyPaging = true)
    {
        Query.AsNoTracking();
        Query.Include(d => d.Restaurant);

        // ---------- фільтри (WHERE) ----------
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim();
            Query.Where(d => d.Name.Contains(search));
        }

        if (q.RestaurantId is { } restaurantId)
            Query.Where(d => d.RestaurantId == restaurantId);

        if (q.CategoryId is { } categoryId)   // фільтр через проміжну сутність M:N → EXISTS (...)
            Query.Where(d => d.DishCategories.Any(dc => dc.CategoryId == categoryId));

        if (q.MinPrice is { } minPrice)
            Query.Where(d => d.Price >= minPrice);

        if (q.MaxPrice is { } maxPrice)
            Query.Where(d => d.Price <= maxPrice);

        if (q.OnlyAvailable)
            Query.Where(d => d.IsAvailable && d.AvailablePortions > 0);

        // ---------- сортування (ORDER BY) — лише дозволені поля; Id — для стабільної пагінації ----------
        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        switch (q.SortBy.ToLowerInvariant())
        {
            case "price":
                if (desc) Query.OrderByDescending(d => d.Price).ThenBy(d => d.Id);
                else Query.OrderBy(d => d.Price).ThenBy(d => d.Id);
                break;
            case "calories":
                if (desc) Query.OrderByDescending(d => d.Calories).ThenBy(d => d.Id);
                else Query.OrderBy(d => d.Calories).ThenBy(d => d.Id);
                break;
            case "createdat":
                if (desc) Query.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id);
                else Query.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id);
                break;
            default:
                if (desc) Query.OrderByDescending(d => d.Name).ThenBy(d => d.Id);
                else Query.OrderBy(d => d.Name).ThenBy(d => d.Id);
                break;
        }

        // ---------- сторінка (OFFSET / FETCH) ----------
        if (applyPaging)
        {
            var pageSize = Paging.ClampPageSize(q.PageSize);
            Query.Skip((q.Page - 1) * pageSize).Take(pageSize);
        }
    }
}
