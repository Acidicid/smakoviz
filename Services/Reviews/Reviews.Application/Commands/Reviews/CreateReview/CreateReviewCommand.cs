using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;

namespace Reviews.Application.Commands.Reviews.CreateReview;

/// <summary>
/// Залишити відгук. Результат — рядковий ObjectId нового документа.
/// TargetType: "dish" (потрібні dishId, dishName), "restaurant" (можна aspects) або "courier".
/// </summary>
public sealed record CreateReviewCommand(
    string TargetType,
    long RestaurantId,
    string RestaurantName,
    long CustomerId,
    string AuthorName,
    int Rating,
    string? Text = null,
    long? DishId = null,
    string? DishName = null,
    AspectsDto? Aspects = null,
    long? OrderId = null,
    string? OrderNumber = null,
    List<string>? Photos = null) : ICommand<string>;
