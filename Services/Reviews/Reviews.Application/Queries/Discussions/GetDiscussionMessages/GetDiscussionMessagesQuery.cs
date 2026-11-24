using FluentValidation;
using MediatR;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;

namespace Reviews.Application.Queries.Discussions.GetDiscussionMessages;

/// <summary>Повна історія обговорення з колекції messages (посилання discussionId) з пагінацією.</summary>
public sealed record GetDiscussionMessagesQuery(string DiscussionId, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IQuery<PagedResult<MessageDto>>;

public sealed class GetDiscussionMessagesQueryHandler(IDiscussionQueries queries)
    : IRequestHandler<GetDiscussionMessagesQuery, PagedResult<MessageDto>>
{
    public async Task<PagedResult<MessageDto>> Handle(GetDiscussionMessagesQuery q, CancellationToken ct)
    {
        if (await queries.GetByIdAsync(q.DiscussionId, ct) is null)
            throw new NotFoundException("Discussion", q.DiscussionId);

        return await queries.GetMessagesAsync(q.DiscussionId, q.Page, q.PageSize, ct);
    }
}

public sealed class GetDiscussionMessagesQueryValidator : AbstractValidator<GetDiscussionMessagesQuery>
{
    public GetDiscussionMessagesQueryValidator()
    {
        RuleFor(x => x.DiscussionId).MustBeObjectId();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}
