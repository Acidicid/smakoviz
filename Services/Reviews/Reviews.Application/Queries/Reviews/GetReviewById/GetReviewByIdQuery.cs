using FluentValidation;
using MediatR;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;

namespace Reviews.Application.Queries.Reviews.GetReviewById;

public sealed record GetReviewByIdQuery(string Id) : IQuery<ReviewDetailsDto>;

/// <summary>Обробник читає через IReviewQueries (проєкція в DTO робиться в MongoDB), а не через IMongoCollection.</summary>
public sealed class GetReviewByIdQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewByIdQuery, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(GetReviewByIdQuery query, CancellationToken ct)
        => await queries.GetByIdAsync(query.Id, ct)
           ?? throw new NotFoundException("Review", query.Id);
}

public sealed class GetReviewByIdQueryValidator : AbstractValidator<GetReviewByIdQuery>
{
    public GetReviewByIdQueryValidator() => RuleFor(x => x.Id).MustBeObjectId();
}
