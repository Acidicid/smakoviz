using Menu.Domain.Common;
using Menu.Domain.Exceptions;

namespace Menu.Domain.Entities;

/// <summary>
/// Ресторан-партнер. Власник назви ресторану в усій платформі
/// (Доставка й Відгуки тримають лише копію/знімок — див. політику дублювання, Лаб. 01).
/// </summary>
public class Restaurant : AuditableEntity
{
    private readonly List<Dish> _dishes = [];

    public long Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string City { get; private set; } = "";
    public string CuisineType { get; private set; } = "";
    public bool IsActive { get; private set; }

    /// <summary>1:1 — адреса, години роботи, мінімальна сума.</summary>
    public RestaurantProfile? Profile { get; private set; }

    /// <summary>1:N — страви ресторану. Змінюється лише через методи, а не напряму.</summary>
    public IReadOnlyCollection<Dish> Dishes => _dishes;

    private Restaurant() { } // для EF Core

    public Restaurant(string name, string slug, string city, string cuisineType)
    {
        Name = Guard.NotEmpty(name, "Name", 150);
        Slug = Guard.Slug(slug);
        City = Guard.NotEmpty(city, "City", 80);
        CuisineType = Guard.NotEmpty(cuisineType, "CuisineType", 50);
        IsActive = true;
    }

    public void Rename(string name) => Name = Guard.NotEmpty(name, "Name", 150);

    public void ChangeCity(string city) => City = Guard.NotEmpty(city, "City", 80);

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        if (!IsActive) throw new DomainException($"Restaurant '{Name}' is already inactive");
        IsActive = false;
    }

    /// <summary>Створити або оновити профіль (1:1).</summary>
    public void SetProfile(string address, string phone, TimeOnly opensAt, TimeOnly closesAt,
        decimal minOrderAmount, string? description)
    {
        if (Profile is null)
            Profile = new RestaurantProfile(address, phone, opensAt, closesAt, minOrderAmount, description);
        else
            Profile.Update(address, phone, opensAt, closesAt, minOrderAmount, description);
    }
}
