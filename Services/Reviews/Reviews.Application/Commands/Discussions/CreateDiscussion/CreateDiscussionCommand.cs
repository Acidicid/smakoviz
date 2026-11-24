using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Commands.Discussions.CreateDiscussion;

/// <summary>
/// Відкрити обговорення біля ресторану або конкретного відгуку.
/// Якщо задано ReviewId — відгук отримує посилання discussionId (дві колекції).
/// </summary>
public sealed record CreateDiscussionCommand(
    long RestaurantId,
    string RestaurantName,
    string Title,
    string? ReviewId = null) : ICommand<string>;

public sealed class CreateDiscussionCommandHandler(IDiscussionRepository discussions, IReviewRepository reviews)
    : IRequestHandler<CreateDiscussionCommand, string>
{
    public async Task<string> Handle(CreateDiscussionCommand cmd, CancellationToken ct)
    {
        Review? review = null;
        if (cmd.ReviewId is not null)
        {
            review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct)
                ?? throw new NotFoundException(nameof(Review), cmd.ReviewId);
        }

        var discussion = new Discussion(cmd.RestaurantId, cmd.RestaurantName, cmd.Title, review?.Id);

        // правило сутності: у відгуку може бути лише одне обговорення (перевіряємо ДО вставки)
        review?.AttachDiscussion(discussion.Id);

        await discussions.AddAsync(discussion, ct);
        if (review is not null)
            await reviews.UpdateAsync(review, ct);

        return discussion.Id.ToString();
    }
}

public sealed class CreateDiscussionCommandValidator : AbstractValidator<CreateDiscussionCommand>
{
    public CreateDiscussionCommandValidator(IReviewRepository reviews)
    {
        RuleFor(x => x.RestaurantId).GreaterThan(0);
        RuleFor(x => x.RestaurantName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ReviewId).MustBeObjectIdIfPresent();

        // асинхронна перевірка в MongoDB: відгук, біля якого відкривають обговорення, існує
        RuleFor(x => x.ReviewId)
            .MustAsync(async (id, ct) => await reviews.ExistsAsync(ObjectId.Parse(id!), ct))
            .When(x => x.ReviewId is not null && ObjectId.TryParse(x.ReviewId, out _))
            .WithMessage("Review '{PropertyValue}' does not exist");
    }
}
