using System.ComponentModel.DataAnnotations;

namespace Menu.Bll.Dtos;

// ---------------- вихідні ----------------

public record RestaurantDto(
    long Id,
    string Name,
    string Slug,
    string City,
    string CuisineType,
    bool IsActive);

public record RestaurantProfileDto(
    string Address,
    string Phone,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    decimal MinOrderAmount,
    string? Description);

/// <summary>Ресторан з профілем і меню (завантажується Explicit Loading).</summary>
public record RestaurantDetailsDto(
    long Id,
    string Name,
    string Slug,
    string City,
    string CuisineType,
    bool IsActive,
    string RowVersion,
    RestaurantProfileDto? Profile,
    IReadOnlyList<DishSummaryDto> Dishes);

// ---------------- вхідні ----------------
// DataAnnotations — лише прості правила (обов'язковість, довжина).
// Складні правила (формат slug і телефону, години роботи) — у FluentValidation (Validation/).

public class CreateRestaurantDto
{
    [Required, StringLength(150)]
    public string Name { get; set; } = "";

    [Required, StringLength(80)]
    public string Slug { get; set; } = "";

    [Required, StringLength(80)]
    public string City { get; set; } = "";

    [Required, StringLength(50)]
    public string CuisineType { get; set; } = "";

    /// <summary>Необов'язково: профіль можна задати й пізніше (PUT /api/restaurants/{id}/profile).</summary>
    public RestaurantProfileInputDto? Profile { get; set; }
}

public class RestaurantProfileInputDto
{
    [Required, StringLength(300)]
    public string Address { get; set; } = "";

    [Required, StringLength(20)]
    public string Phone { get; set; } = "";

    /// <summary>Формат "09:00".</summary>
    public TimeOnly OpensAt { get; set; }

    /// <summary>Формат "22:00". Може бути «після опівночі», напр. 02:00.</summary>
    public TimeOnly ClosesAt { get; set; }

    public decimal MinOrderAmount { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }
}

/// <summary>
/// Параметри списку ресторанів (GET /api/restaurants?...): пагінація, фільтри, сортування.
/// Дозволені поля сортування перевіряє RestaurantQueryValidator.
/// </summary>
public class RestaurantQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    /// <summary>1..100 (більше — обрізається до 100).</summary>
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    /// <summary>Пошук за назвою.</summary>
    [StringLength(100)]
    public string? Search { get; set; }

    [StringLength(80)]
    public string? City { get; set; }

    [StringLength(50)]
    public string? CuisineType { get; set; }

    public bool OnlyActive { get; set; } = true;

    /// <summary>name | city | createdAt</summary>
    public string SortBy { get; set; } = "name";

    /// <summary>asc | desc</summary>
    public string SortDir { get; set; } = "asc";
}
