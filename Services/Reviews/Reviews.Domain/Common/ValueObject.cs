namespace Reviews.Domain.Common;

/// <summary>
/// Базовий Value Object (як ValueObject в eShop, Ordering.Domain/SeedWork):
/// об'єкти рівні, якщо рівні ВСІ компоненти (GetEqualityComponents), а не посилання.
/// Нащадки — незмінні: без setter-ів, валідація в конструкторі.
/// </summary>
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
        => obj is ValueObject other
           && GetType() == other.GetType()
           && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override int GetHashCode()
        => GetEqualityComponents().Aggregate(0, (hash, part) => HashCode.Combine(hash, part));

    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);
    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
}
