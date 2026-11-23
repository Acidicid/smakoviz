namespace Delivery.Domain.Models;

/// <summary>Сторінка результатів пошуку + загальна кількість (для пагінації).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
