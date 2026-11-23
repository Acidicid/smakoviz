namespace Menu.Domain.Entities;

/// <summary>
/// Явна проміжна сутність M:N «страва ↔ категорія» зі складеним ключем (DishId, CategoryId)
/// і ВЛАСНИМИ полями — те, чого не дає автоматичне M:N в EF Core:
///   AddedAt    — коли страву додали в категорію (напр. «Хіт продажу» з 01.10);
///   IsFeatured — показувати першою в категорії.
/// </summary>
public class DishCategory
{
    public long DishId { get; private set; }
    public Dish Dish { get; private set; } = null!;
    public int CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    public DateTime AddedAt { get; private set; }
    public bool IsFeatured { get; private set; }

    private DishCategory() { } // для EF Core

    internal DishCategory(Dish dish, int categoryId, bool isFeatured, DateTime addedAt)
    {
        Dish = dish;
        DishId = dish.Id;
        CategoryId = categoryId;
        IsFeatured = isFeatured;
        AddedAt = addedAt;
    }

    public void Feature() => IsFeatured = true;
    public void Unfeature() => IsFeatured = false;
}
