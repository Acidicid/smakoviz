using AutoMapper;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;

using Reviews.Application.Caching;

namespace Reviews.Application.Commands.Reviews.AddComment;

/// <summary>Додати коментар у вкладений масив comments відгуку.</summary>
public sealed record AddCommentCommand(string ReviewId, string AuthorName, string Text) : ICommand<ReviewDetailsDto>;

public sealed class AddCommentCommandHandler(IReviewRepository reviews, IMapper mapper, ITwoLevelCache cache)
    : IRequestHandler<AddCommentCommand, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct)
            ?? throw new NotFoundException(nameof(Review), cmd.ReviewId);

        review.AddComment(cmd.AuthorName, cmd.Text);   // ліміт 50 коментарів — правило сутності
        await reviews.UpdateAsync(review, ct);         // з перевіркою версії
        await cache.InvalidateAsync("ReviewCommentAdded", ct, ReviewsCacheKeys.Review(cmd.ReviewId));

        return mapper.Map<ReviewDetailsDto>(review);
    }
}

public sealed class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.ReviewId).MustBeObjectId();
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Comment.MaxTextLength);
    }
}
