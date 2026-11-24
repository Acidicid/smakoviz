using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
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
public sealed class CreateReviewCommandHandler(IReviewRepository reviews, ILogger<CreateReviewCommandHandler> logger)
    : IRequestHandler<CreateReviewCommand, string>
{
    /// <summary>Власні spans бізнес-операцій (Лаб. 05), підключені в ServiceDefaults через AddSource("SmakoViz.*").</summary>
    public static readonly ActivitySource ActivitySource = new("SmakoViz.Reviews");

    public async Task<string> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("CreateReview");
        activity?.SetTag("restaurant.id", cmd.RestaurantId);
        activity?.SetTag("review.target", cmd.TargetType);

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

        activity?.SetTag("review.id", review.Id.ToString());
        // Бізнес-подія: без тексту відгуку й імені автора — лише ідентифікатори та оцінка
        logger.LogInformation("Review {ReviewId} created for {TargetType} of restaurant {RestaurantId}, rating {Rating}",
            review.Id.ToString(), cmd.TargetType, cmd.RestaurantId, cmd.Rating);

        return review.Id.ToString();
    }
}
