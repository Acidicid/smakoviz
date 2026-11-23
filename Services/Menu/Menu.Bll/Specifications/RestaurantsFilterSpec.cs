using Ardalis.Specification;
using Menu.Bll.Dtos;
using Menu.Domain.Entities;

namespace Menu.Bll.Specifications;

/// <summary>
/// ДРУГА специфікація з іншими критеріями (місто, кухня, активність) — працює через той самий
/// GenericRepository.ListAsync / WithSpecification без жодних змін у репозиторії.
/// </summary>
public sealed class RestaurantsFilterSpec : Specification<Restaurant>
{
    public static readonly string[] SortFields = ["name", "city", "createdAt"];

    public RestaurantsFilterSpec(RestaurantQuery q, bool applyPaging = true)
    {
        Query.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim();
            Query.Where(r => r.Name.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(q.City))
        {
            var city = q.City.Trim();
            Query.Where(r => r.City == city);
        }

        if (!string.IsNullOrWhiteSpace(q.CuisineType))
        {
            var cuisine = q.CuisineType.Trim();
            Query.Where(r => r.CuisineType == cuisine);
        }

        if (q.OnlyActive)
            Query.Where(r => r.IsActive);

        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        switch (q.SortBy.ToLowerInvariant())
        {
            case "city":
                if (desc) Query.OrderByDescending(r => r.City).ThenBy(r => r.Name);
                else Query.OrderBy(r => r.City).ThenBy(r => r.Name);
                break;
            case "createdat":
                if (desc) Query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id);
                else Query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id);
                break;
            default:
                if (desc) Query.OrderByDescending(r => r.Name).ThenBy(r => r.Id);
                else Query.OrderBy(r => r.Name).ThenBy(r => r.Id);
                break;
        }

        if (applyPaging)
        {
            var pageSize = Paging.ClampPageSize(q.PageSize);
            Query.Skip((q.Page - 1) * pageSize).Take(pageSize);
        }
    }
}
