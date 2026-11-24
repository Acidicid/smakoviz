using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Caching;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Commands.Reviews.DeleteReview;

/// <summary>Видалити відгук (команда без результату). ExpectedVersion — необов'язковий If-Match.</summary>
public sealed record DeleteReviewCommand(string Id, int? ExpectedVersion = null) : ICommand;

public sealed class DeleteReviewCommandHandler(IReviewRepository reviews, ITwoLevelCache cache) : IRequestHandler<DeleteReviewCommand>
{
    public async Task Handle(DeleteReviewCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.Id);
        var existing = await reviews.GetByIdAsync(id, ct);   // Лаб. 06: потрібен RestaurantId для інвалідації рейтингу
        if (await reviews.DeleteAsync(id, cmd.ExpectedVersion, ct))
        {
            var keys = existing is null
                ? new[] { ReviewsCacheKeys.Review(cmd.Id) }
                : new[] { ReviewsCacheKeys.Review(cmd.Id), ReviewsCacheKeys.RestaurantRating(existing.RestaurantId) };
            await cache.InvalidateAsync("ReviewDeleted", ct, keys);
            return;
        }

        // нічого не видалено: або документа немає, або його версія вже інша
        if (!await reviews.ExistsAsync(id, ct))
            throw new NotFoundException(nameof(Review), cmd.Id);
        throw new ConflictException($"Review {cmd.Id} was modified (expected version {cmd.ExpectedVersion}). Reload and try again");
    }
}

public sealed class DeleteReviewCommandValidator : AbstractValidator<DeleteReviewCommand>
{
    public DeleteReviewCommandValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0).When(x => x.ExpectedVersion is not null);
    }
}
