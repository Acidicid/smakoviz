using MediatR;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Commands.Reviews.CreateReview;

/// <summary>
/// Обробник працює лише через IReviewRepository (інтерфейс Domain) — про MongoDB нічого не знає.
/// Сюди потрапляють лише провалідовані команди (ValidationBehavior); бізнес-правила самої сутності
/// перевіряють конструктори Review і Value Objects.
/// </summary>
public sealed class CreateReviewCommandHandler(IReviewRepository reviews)
    : IRequestHandler<CreateReviewCommand, string>
{
    public async Task<string> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        var aspects = cmd.Aspects is null
            ? null
            : new RatingAspects(cmd.Aspects.Food, cmd.Aspects.Packaging, cmd.Aspects.PortionSize);

        var review = new Review(
            EnumText.Target(cmd.TargetType),
            cmd.RestaurantId,
            cmd.RestaurantName,
            new Author(cmd.CustomerId, cmd.AuthorName),
            new Rating(cmd.Rating),
            cmd.Text,
            cmd.DishId,
            cmd.DishName,
            aspects,
            cmd.OrderId,
            cmd.OrderNumber,
            cmd.Photos);

        await reviews.AddAsync(review, ct);
        return review.Id.ToString();
    }
}
