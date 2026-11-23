using Menu.Domain.Exceptions;

namespace Menu.Domain.Common;

/// <summary>Перевірки інваріантів у конструкторах і методах сутностей.</summary>
internal static class Guard
{
    public static string NotEmpty(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required");
        value = value.Trim();
        if (value.Length > maxLength)
            throw new DomainException($"{field} must be at most {maxLength} characters");
        return value;
    }

    public static decimal Positive(decimal value, string field)
    {
        if (value <= 0)
            throw new DomainException($"{field} must be positive");
        return value;
    }

    public static string Slug(string? value)
    {
        var slug = NotEmpty(value, "Slug", 80).ToLowerInvariant();
        if (slug.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new DomainException("Slug may contain only latin letters, digits and '-'");
        return slug;
    }
}
