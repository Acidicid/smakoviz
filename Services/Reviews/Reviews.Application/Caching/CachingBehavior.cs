using MediatR;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Caching;

/// <summary>
/// Запит, результат якого можна кешувати. Ключ — з параметрів запиту (передбачувана схема reviews:...),
/// політика — TTL за типом даних. Позначка ставиться лише на query (CQRS: команди не кешуються).
/// </summary>
public interface ICacheableQuery
{
    string CacheKey { get; }
    CachePolicy CachePolicy { get; }
}

/// <summary>Ключі відгуків — префікс reviews: ізолює їх від інших сервісів у спільному Redis.</summary>
public static class ReviewsCacheKeys
{
    public static string Review(string id) => $"reviews:review:{id}";
    public static string RestaurantRating(long restaurantId) => $"reviews:restaurant:{restaurantId}:rating";
}

/// <summary>
/// Pipeline behavior кешування (cache-aside): для ICacheableQuery результат береться з L1 → L2 → обробник (MongoDB).
/// Стоїть ПІСЛЯ ValidationBehavior — некоректний запит не потрапить ні в кеш, ні в базу.
/// Інвалідацію виконують обробники команд одразу після запису (Create/Update/Delete/AddComment).
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(ITwoLevelCache cache, ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not ICacheableQuery cacheable)
            return await next(ct);

        logger.LogDebug("{RequestName} is cacheable, key {CacheKey}", typeof(TRequest).Name, cacheable.CacheKey);
        var result = await cache.GetOrCreateAsync<TResponse>(cacheable.CacheKey, async _ => await next(ct),
            cacheable.CachePolicy, ct);
        return result!;
    }
}
