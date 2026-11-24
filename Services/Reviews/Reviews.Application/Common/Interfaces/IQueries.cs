using Reviews.Application.Common.Models;

namespace Reviews.Application.Common.Interfaces;

/// <summary>
/// Сторона ЧИТАННЯ (CQRS). Обробники запитів не звертаються до MongoDB напряму —
/// викликають цей інтерфейс; реалізація в Infrastructure робить проєкцію одразу в DTO
/// ($project), фільтрацію $match, сортування й skip/limit на боці MongoDB.
/// (Так само в eShop: IOrderQueries.)
/// </summary>
public interface IReviewQueries
{
    Task<ReviewDetailsDto?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<PagedResult<ReviewListItemDto>> GetListAsync(ReviewListFilter filter, CancellationToken ct = default);

    Task<RestaurantRatingDto> GetRestaurantRatingAsync(long restaurantId, CancellationToken ct = default);
}

/// <summary>Критерії списку відгуків (уже провалідовані).</summary>
public sealed record ReviewListFilter(
    long? RestaurantId,
    long? DishId,
    string? TargetType,
    int? MinRating,
    long? CustomerId,
    string? Search,
    string SortBy,
    bool Descending,
    int Page,
    int PageSize);

public interface IDiscussionQueries
{
    Task<DiscussionDto?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<PagedResult<MessageDto>> GetMessagesAsync(string discussionId, int page, int pageSize,
        CancellationToken ct = default);
}
