using FluentValidation;
using MediatR;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;

namespace Reviews.Application.Queries.Discussions.GetDiscussionById;

/// <summary>Обговорення з вбудованим прев'ю останніх повідомлень (одне читання, без $lookup).</summary>
public sealed record GetDiscussionByIdQuery(string Id) : IQuery<DiscussionDto>;

public sealed class GetDiscussionByIdQueryHandler(IDiscussionQueries queries)
    : IRequestHandler<GetDiscussionByIdQuery, DiscussionDto>
{
    public async Task<DiscussionDto> Handle(GetDiscussionByIdQuery query, CancellationToken ct)
        => await queries.GetByIdAsync(query.Id, ct)
           ?? throw new NotFoundException("Discussion", query.Id);
}

public sealed class GetDiscussionByIdQueryValidator : AbstractValidator<GetDiscussionByIdQuery>
{
    public GetDiscussionByIdQueryValidator() => RuleFor(x => x.Id).MustBeObjectId();
}
