using Menu.Bll.Dtos;

namespace Menu.Api.Infrastructure;

/// <summary>
/// Метадані пагінації дублюються в заголовках (крім тіла): клієнти й кеш-проксі
/// можуть прочитати їх без розбору JSON.
/// </summary>
public static class PaginationHeaders
{
    public static void AddPagination<T>(this HttpResponse response, PagedResult<T> page)
    {
        response.Headers["X-Total-Count"] = page.TotalCount.ToString();
        response.Headers["X-Total-Pages"] = page.TotalPages.ToString();
        response.Headers["X-Page"] = page.Page.ToString();
        response.Headers["X-Page-Size"] = page.PageSize.ToString();
    }
}
