namespace Reviews.Domain.Enums;

/// <summary>На що залишено відгук. У БД зберігається рядком "dish" / "restaurant" / "courier" (як у Лаб. 01).</summary>
public enum ReviewTarget
{
    Dish,
    Restaurant,
    Courier
}

/// <summary>Хто пише в обговоренні. У БД: "customer" / "restaurant" / "moderator".</summary>
public enum MessageRole
{
    Customer,
    Restaurant,
    Moderator
}
