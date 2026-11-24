using FluentValidation;
using MediatR;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;

namespace Reviews.Application.Queries.Reviews.GetRestaurantRating;

/// <summary>Середня оцінка ресторану й розбивка за типами відгуків (aggregation pipeline в MongoDB).</summary>
public sealed record GetRestaurantRatingQuery(long RestaurantId) : IQuery<RestaurantRatingDto>;

public sealed class GetRestaurantRatingQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetRestaurantRatingQuery, RestaurantRatingDto>
{
    public Task<RestaurantRatingDto> Handle(GetRestaurantRatingQuery query, CancellationToken ct)
        => queries.GetRestaurantRatingAsync(query.RestaurantId, ct);
}

public sealed class GetRestaurantRatingQueryValidator : AbstractValidator<GetRestaurantRatingQuery>
{
    public GetRestaurantRatingQueryValidator() => RuleFor(x => x.RestaurantId).GreaterThan(0);
}
