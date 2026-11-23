using Delivery.Domain.Exceptions;

namespace Delivery.Bll.Services;

/// <summary>Спільні перевірки вхідних даних для сервісів.</summary>
internal static class ServiceGuards
{
    public const int MaxPageSize = 100;

    /// <summary>Хто змінює дані — пишеться в аудитні колонки (у Лаб. 06 — з JWT).</summary>
    public const string CurrentUser = "delivery-api";

    public static void Paging(int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["Page must be >= 1"];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}"];
        if (errors.Count > 0) throw new ValidationException("Invalid paging parameters", errors);
    }

    /// <summary>RowVersion приходить з клієнта як base64; некоректний рядок — помилка валідації (400).</summary>
    public static byte[] RowVersion(string value, string field = "rowVersion")
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException)
        {
            // впадемо нижче з однаковим повідомленням
        }

        throw new ValidationException("Invalid RowVersion",
            new Dictionary<string, string[]> { [field] = ["RowVersion must be the base64 value returned by GET"] });
    }
}
