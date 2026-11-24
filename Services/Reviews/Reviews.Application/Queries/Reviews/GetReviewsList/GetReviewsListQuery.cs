using FluentValidation;
using MediatR;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Queries.Reviews.GetReviewsList;

/// <summary>
/// Список відгуків з фільтрами, сортуванням і пагінацією — усе виконує MongoDB
/// ($match, $sort, $skip, $limit), у пам'ять потрапляє лише сторінка.
/// </summary>
public sealed record GetReviewsListQuery(
    long? RestaurantId = null,
    long? DishId = null,
    string? TargetType = null,
    int? MinRating = null,
    long? CustomerId = null,
    string? Search = null,
    string SortBy = "createdAt",
    string SortDir = "desc",
    int Page = 1,
    int PageSize = Paging.DefaultPageSize) : IQuery<PagedResult<ReviewListItemDto>>;

public sealed class GetReviewsListQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewsListQuery, PagedResult<ReviewListItemDto>>
{
    public Task<PagedResult<ReviewListItemDto>> Handle(GetReviewsListQuery q, CancellationToken ct)
        => queries.GetListAsync(new ReviewListFilter(
            q.RestaurantId,
            q.DishId,
            q.TargetType?.ToLowerInvariant(),
            q.MinRating,
            q.CustomerId,
            string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
            q.SortBy.Equals("rating", StringComparison.OrdinalIgnoreCase) ? "rating" : "createdAt",
            !q.SortDir.Equals("asc", StringComparison.OrdinalIgnoreCase),
            q.Page,
            Math.Clamp(q.PageSize, 1, Paging.MaxPageSize)), ct);
}

public sealed class GetReviewsListQueryValidator : AbstractValidator<GetReviewsListQuery>
{
    private static readonly string[] SortFields = ["createdAt", "rating"];

    public GetReviewsListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.RestaurantId).GreaterThan(0).When(x => x.RestaurantId is not null);
        RuleFor(x => x.DishId).GreaterThan(0).When(x => x.DishId is not null);
        RuleFor(x => x.CustomerId).GreaterThan(0).When(x => x.CustomerId is not null);
        RuleFor(x => x.MinRating).InclusiveBetween(Rating.Min, Rating.Max).When(x => x.MinRating is not null);
        RuleFor(x => x.TargetType!).MustBeEnum<GetReviewsListQuery, ReviewTarget>().When(x => x.TargetType is not null);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.SortBy)
            .Must(s => SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"sortBy must be one of: {string.Join(", ", SortFields)}");
        RuleFor(x => x.SortDir)
            .Must(d => d.Equals("asc", StringComparison.OrdinalIgnoreCase) || d.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortDir must be 'asc' or 'desc'");
    }
}
