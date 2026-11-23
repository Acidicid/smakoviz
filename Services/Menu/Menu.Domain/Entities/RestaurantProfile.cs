using Menu.Domain.Common;
using Menu.Domain.Exceptions;

namespace Menu.Domain.Entities;

/// <summary>Профіль ресторану — 1:1 з Restaurant (RestaurantId — водночас PK і FK).</summary>
public class RestaurantProfile
{
    public long RestaurantId { get; private set; }
    public string Address { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public TimeOnly OpensAt { get; private set; }
    public TimeOnly ClosesAt { get; private set; }
    public decimal MinOrderAmount { get; private set; }
    public string? Description { get; private set; }

    private RestaurantProfile() { } // для EF Core

    internal RestaurantProfile(string address, string phone, TimeOnly opensAt, TimeOnly closesAt,
        decimal minOrderAmount, string? description)
        => Update(address, phone, opensAt, closesAt, minOrderAmount, description);

    internal void Update(string address, string phone, TimeOnly opensAt, TimeOnly closesAt,
        decimal minOrderAmount, string? description)
    {
        if (opensAt == closesAt)
            throw new DomainException("Opening and closing time must differ");
        if (minOrderAmount < 0)
            throw new DomainException("Minimum order amount cannot be negative");

        Address = Guard.NotEmpty(address, "Address", 300);
        Phone = Guard.NotEmpty(phone, "Phone", 20);
        OpensAt = opensAt;
        ClosesAt = closesAt;
        MinOrderAmount = minOrderAmount;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    /// <summary>Чи працює ресторан у заданий час (враховує роботу після опівночі).</summary>
    public bool IsOpenAt(TimeOnly time) =>
        OpensAt < ClosesAt
            ? time >= OpensAt && time < ClosesAt
            : time >= OpensAt || time < ClosesAt;
}
