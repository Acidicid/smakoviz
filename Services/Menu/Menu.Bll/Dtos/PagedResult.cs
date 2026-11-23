namespace Menu.Bll.Dtos;

/// <summary>
/// Контракт відповіді для колекцій: сторінка елементів + метадані.
/// Ті самі метадані API дублює в заголовках X-Total-Count / X-Total-Pages (зручно для кешування).
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

/// <summary>Спільні межі пагінації.</summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>pageSize завжди в межах 1..100, навіть якщо клієнт попросив 100000.</summary>
    public static int ClampPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);
}
