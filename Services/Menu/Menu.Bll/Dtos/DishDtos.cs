using System.ComponentModel.DataAnnotations;

namespace Menu.Bll.Dtos;

// ---------------- вихідні ----------------

/// <summary>Картка страви (Eager Loading: ресторан + категорії одним запитом).</summary>
public record DishDto(
    long Id,
    long RestaurantId,
    string RestaurantName,
    string Name,
    string? Description,
    decimal Price,
    int? WeightGrams,
    int? Calories,
    bool IsAvailable,
    int AvailablePortions,
    IReadOnlyList<DishCategoryDto> Categories,
    string RowVersion);

/// <summary>Страва в списку (пагінація) — без категорій, щоб не тягнути зайві JOIN-и.</summary>
public record DishListItemDto(
    long Id,
    long RestaurantId,
    string RestaurantName,
    string Name,
    decimal Price,
    int? Calories,
    bool IsAvailable,
    int AvailablePortions);

/// <summary>Страва в меню ресторану.</summary>
public record DishSummaryDto(long Id, string Name, decimal Price, bool IsAvailable, int AvailablePortions);

/// <summary>Зв'язок страви з категорією — з ВЛАСНИМИ полями проміжної сутності.</summary>
public record DishCategoryDto(int CategoryId, string CategoryName, DateTime AddedAt, bool IsFeatured);

// ---------------- вхідні ----------------

public class CreateDishDto
{
    [Range(1, long.MaxValue)]
    public long RestaurantId { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int? WeightGrams { get; set; }

    public int? Calories { get; set; }

    /// <summary>Скільки порцій приготовано на сьогодні.</summary>
    public int InitialPortions { get; set; }

    /// <summary>Id категорій (довідник: GET /api/categories).</summary>
    public List<int> CategoryIds { get; set; } = [];
}

public class ChangePriceDto
{
    public decimal NewPrice { get; set; }

    /// <summary>Необов'язково: RowVersion з останнього GET — тоді паралельна зміна ціни дасть 409.</summary>
    public string? RowVersion { get; set; }
}

public class PortionsDto
{
    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public class AvailabilityDto
{
    public bool IsAvailable { get; set; }
}

public class AddDishToCategoryDto
{
    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }

    public bool IsFeatured { get; set; }
}

/// <summary>
/// Параметри списку страв (GET /api/dishes?...): пагінація, фільтри, сортування.
/// Усе виконується в SQL (DishesFilterSpec), а не в пам'яті.
/// </summary>
public class DishQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    /// <summary>1..100 (більше — обрізається до 100).</summary>
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    /// <summary>Пошук за назвою страви.</summary>
    [StringLength(100)]
    public string? Search { get; set; }

    public long? RestaurantId { get; set; }

    public int? CategoryId { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    public bool OnlyAvailable { get; set; }

    /// <summary>name | price | calories | createdAt</summary>
    public string SortBy { get; set; } = "name";

    /// <summary>asc | desc</summary>
    public string SortDir { get; set; } = "asc";
}
