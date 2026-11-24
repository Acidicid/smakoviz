namespace Reviews.Application.Common.Models;

/// <summary>Сторінка результатів + метадані пагінації.</summary>
public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required long TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}
