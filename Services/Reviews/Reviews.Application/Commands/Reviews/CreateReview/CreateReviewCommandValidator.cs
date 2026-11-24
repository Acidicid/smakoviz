using FluentValidation;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Commands.Reviews.CreateReview;

public sealed class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator(IReviewRepository reviews)
    {
        RuleFor(x => x.TargetType).NotEmpty().MustBeEnum<CreateReviewCommand, ReviewTarget>();
        RuleFor(x => x.RestaurantId).GreaterThan(0);
        RuleFor(x => x.RestaurantName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);
        RuleFor(x => x.Text).MaximumLength(Review.MaxTextLength);
        RuleFor(x => x.OrderId).GreaterThan(0).When(x => x.OrderId is not null);
        RuleFor(x => x.OrderNumber).MaximumLength(20);

        // гнучка схема: dishId/dishName — лише для відгуку на страву
        When(x => IsTarget(x, ReviewTarget.Dish), () =>
        {
            RuleFor(x => x.DishId).NotNull().GreaterThan(0);
            RuleFor(x => x.DishName).NotEmpty().MaximumLength(150);
        }).Otherwise(() =>
        {
            RuleFor(x => x.DishId).Null().WithMessage("dishId is allowed only for a dish review");
        });

        RuleFor(x => x.Aspects)
            .Null()
            .When(x => !IsTarget(x, ReviewTarget.Restaurant))
            .WithMessage("aspects are allowed only for a restaurant review");
        RuleFor(x => x.Aspects!.Food).InclusiveBetween(Rating.Min, Rating.Max).When(x => x.Aspects?.Food is not null);
        RuleFor(x => x.Aspects!.Packaging).InclusiveBetween(Rating.Min, Rating.Max).When(x => x.Aspects?.Packaging is not null);
        RuleFor(x => x.Aspects!.PortionSize).InclusiveBetween(Rating.Min, Rating.Max).When(x => x.Aspects?.PortionSize is not null);

        RuleFor(x => x.Photos!.Count)
            .LessThanOrEqualTo(Review.MaxPhotos)
            .When(x => x.Photos is not null)
            .WithMessage($"At most {Review.MaxPhotos} photos are allowed");

        // АСИНХРОННЕ правило із запитом до MongoDB: один клієнт — один відгук на ту саму ціль.
        // Друга лінія захисту — unique-індекс ux_reviews_author_target (паралельні запити → 409).
        RuleFor(x => x)
            .MustAsync(async (cmd, ct) => !await reviews.ExistsForTargetAsync(
                cmd.CustomerId, EnumText.Target(cmd.TargetType), cmd.RestaurantId, cmd.DishId, ct))
            .When(x => x.CustomerId > 0 && EnumText.TryParse<ReviewTarget>(x.TargetType, out _))
            .WithName("review")
            .WithMessage("You have already reviewed this target");
    }

    private static bool IsTarget(CreateReviewCommand cmd, ReviewTarget target)
        => EnumText.TryParse<ReviewTarget>(cmd.TargetType, out var parsed) && parsed == target;
}
