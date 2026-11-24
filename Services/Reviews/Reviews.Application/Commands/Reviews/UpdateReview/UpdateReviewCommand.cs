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
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Commands.Reviews.UpdateReview;

/// <summary>
/// Редагувати відгук. ExpectedVersion (із заголовка If-Match або тіла) — оптимістична конкурентність:
/// якщо документ уже змінив хтось інший, буде 409 замість «тихого» перезапису.
/// </summary>
public sealed record UpdateReviewCommand(
    string Id,
    int Rating,
    string? Text,
    AspectsDto? Aspects,
    int? ExpectedVersion) : ICommand<ReviewDetailsDto>;

public sealed class UpdateReviewCommandHandler(IReviewRepository reviews, IMapper mapper)
    : IRequestHandler<UpdateReviewCommand, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(UpdateReviewCommand cmd, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.Id), ct)
            ?? throw new NotFoundException(nameof(Review), cmd.Id);

        if (cmd.ExpectedVersion is { } expected && expected != review.Version)
            throw new ConflictException(
                $"Review {cmd.Id} has version {review.Version}, but version {expected} was expected. Reload and try again");

        var aspects = cmd.Aspects is null
            ? null
            : new RatingAspects(cmd.Aspects.Food, cmd.Aspects.Packaging, cmd.Aspects.PortionSize);

        review.Edit(new Rating(cmd.Rating), cmd.Text, aspects);
        await reviews.UpdateAsync(review, ct);   // ReplaceOne лише якщо version у БД не змінилась

        return mapper.Map<ReviewDetailsDto>(review);
    }
}

public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);
        RuleFor(x => x.Text).MaximumLength(Review.MaxTextLength);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0).When(x => x.ExpectedVersion is not null);
        RuleFor(x => x.Aspects!.Food).InclusiveBetween(Rating.Min, Rating.Max).When(x => x.Aspects?.Food is not null);
        RuleFor(x => x.Aspects!.Packaging).InclusiveBetween(Rating.Min, Rating.Max).When(x => x.Aspects?.Packaging is not null);
        RuleFor(x => x.Aspects!.PortionSize).InclusiveBetween(Rating.Min, Rating.Max).When(x => x.Aspects?.PortionSize is not null);
    }
}
