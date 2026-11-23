using System.ComponentModel.DataAnnotations;

namespace Menu.Bll.Dtos;

public record CategoryDto(int Id, string Name, string Slug, int SortOrder);

/// <summary>Категорія + скільки в ній доступних страв (агрегація по проміжній таблиці DishCategories).</summary>
public record CategoryWithStatsDto(int Id, string Name, string Slug, int SortOrder, int AvailableDishes);

public class CreateCategoryDto
{
    [Required, StringLength(80)]
    public string Name { get; set; } = "";

    [Required, StringLength(80)]
    public string Slug { get; set; } = "";

    [Range(0, 10000)]
    public int SortOrder { get; set; }
}
