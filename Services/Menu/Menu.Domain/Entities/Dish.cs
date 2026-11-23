using Menu.Domain.Common;
using Menu.Domain.Exceptions;

namespace Menu.Domain.Entities;

/// <summary>
/// Страва — аналог CatalogItem з eShop. Усі зміни стану — лише методами з перевіркою правил:
///   ціна задається в конструкторі й змінюється ChangePrice (завжди &gt; 0);
///   денний запас порцій змінюється лише AddPortions / RemovePortions (аналог AddStock / RemoveStock);
///   категорії — AddToCategory / RemoveFromCategory.
/// Власник назви й ціни в усій платформі; Доставка тримає копію (Лаб. 01).
/// </summary>
public class Dish : AuditableEntity
{
    private readonly List<DishCategory> _dishCategories = [];

    public long Id { get; private set; }
    public long RestaurantId { get; private set; }
    public Restaurant Restaurant { get; private set; } = null!;
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public int? WeightGrams { get; private set; }
    public int? Calories { get; private set; }
    public bool IsAvailable { get; private set; }

    /// <summary>Скільки порцій ще можна продати сьогодні (аналог AvailableStock).</summary>
    public int AvailablePortions { get; private set; }

    /// <summary>M:N з Category через явну проміжну сутність (з полями AddedAt і IsFeatured).</summary>
    public IReadOnlyCollection<DishCategory> DishCategories => _dishCategories;

    private Dish() { } // для EF Core

    public Dish(long restaurantId, string name, decimal price,
        string? description = null, int? weightGrams = null, int? calories = null)
    {
        if (restaurantId <= 0) throw new DomainException("Restaurant is required");
        RestaurantId = restaurantId;
        Name = Guard.NotEmpty(name, "Name", 150);
        Price = Guard.Positive(price, "Price");
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SetNutrition(weightGrams, calories);
        IsAvailable = true;
    }

    public void Rename(string name) => Name = Guard.NotEmpty(name, "Name", 150);

    public void ChangePrice(decimal newPrice)
    {
        Guard.Positive(newPrice, "Price");
        if (newPrice == Price)
            throw new DomainException($"Dish '{Name}' already costs {Price}");
        Price = newPrice;
    }

    public void SetNutrition(int? weightGrams, int? calories)
    {
        if (weightGrams is <= 0) throw new DomainException("Weight must be positive");
        if (calories is < 0 or > 5000) throw new DomainException("Calories must be between 0 and 5000");
        WeightGrams = weightGrams;
        Calories = calories;
    }

    public void MarkAvailable() => IsAvailable = true;

    public void MarkUnavailable() => IsAvailable = false;

    /// <summary>Ресторан приготував ще порції (аналог AddStock).</summary>
    public void AddPortions(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive");
        AvailablePortions += quantity;
        IsAvailable = true;
    }

    /// <summary>Списати порції при продажу (аналог RemoveStock). Не можна піти в мінус.</summary>
    public void RemovePortions(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive");
        if (!IsAvailable) throw new DomainException($"Dish '{Name}' is not available");
        if (AvailablePortions < quantity)
            throw new DomainException($"Only {AvailablePortions} portions of '{Name}' available");

        AvailablePortions -= quantity;
        if (AvailablePortions == 0) IsAvailable = false;   // закінчилось — знімаємо з продажу
    }

    public DishCategory AddToCategory(int categoryId, bool isFeatured, DateTime addedAt)
    {
        if (_dishCategories.Any(dc => dc.CategoryId == categoryId))
            throw new DomainException($"Dish '{Name}' is already in category {categoryId}");

        var link = new DishCategory(this, categoryId, isFeatured, addedAt);
        _dishCategories.Add(link);
        return link;
    }

    public void RemoveFromCategory(int categoryId)
    {
        var link = _dishCategories.FirstOrDefault(dc => dc.CategoryId == categoryId)
            ?? throw new DomainException($"Dish '{Name}' is not in category {categoryId}");
        _dishCategories.Remove(link);
    }
}
