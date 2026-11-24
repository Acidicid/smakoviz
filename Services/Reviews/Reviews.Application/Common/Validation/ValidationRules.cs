using FluentValidation;
using MongoDB.Bson;
using Reviews.Domain.Enums;

namespace Reviews.Application.Common.Validation;

/// <summary>Спільні правила, у т.ч. MongoDB-специфічні (коректний рядок ObjectId).</summary>
public static class ValidationRules
{
    public static IRuleBuilderOptions<T, string> MustBeObjectId<T>(this IRuleBuilder<T, string> rule)
        => rule.Must(id => ObjectId.TryParse(id, out var parsed) && parsed != ObjectId.Empty)
            .WithMessage("'{PropertyName}' must be a valid 24-character hex ObjectId");

    public static IRuleBuilderOptions<T, string?> MustBeObjectIdIfPresent<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(id => id is null || ObjectId.TryParse(id, out _))
            .WithMessage("'{PropertyName}' must be a valid 24-character hex ObjectId");

    public static IRuleBuilderOptions<T, string> MustBeEnum<T, TEnum>(this IRuleBuilder<T, string> rule)
        where TEnum : struct, Enum
        => rule.Must(value => EnumText.TryParse<TEnum>(value, out _))
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", EnumText.All<TEnum>())}");
}

/// <summary>Перетворення enum ↔ рядок у тому ж вигляді, що й у БД ("dish", "restaurant", ...).</summary>
public static class EnumText
{
    public static string ToText<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    public static bool TryParse<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
        => Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value)
           && !int.TryParse(text, out _);

    public static IEnumerable<string> All<TEnum>() where TEnum : struct, Enum
        => Enum.GetValues<TEnum>().Select(ToText);

    public static ReviewTarget Target(string text) => Enum.Parse<ReviewTarget>(text, ignoreCase: true);
}
