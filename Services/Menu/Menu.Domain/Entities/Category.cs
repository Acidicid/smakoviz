using Menu.Domain.Common;

namespace Menu.Domain.Entities;

/// <summary>Довідник категорій (Піца, Напої, Хіт продажу...). Засівається HasData з фіксованими Id.</summary>
public class Category
{
    private readonly List<DishCategory> _dishCategories = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public int SortOrder { get; private set; }

    /// <summary>M:N зі стравами через DishCategory.</summary>
    public IReadOnlyCollection<DishCategory> DishCategories => _dishCategories;

    private Category() { } // для EF Core

    public Category(string name, string slug, int sortOrder)
    {
        Name = Guard.NotEmpty(name, "Name", 80);
        Slug = Guard.Slug(slug);
        SortOrder = sortOrder >= 0 ? sortOrder : throw new Exceptions.DomainException("SortOrder cannot be negative");
    }
}
