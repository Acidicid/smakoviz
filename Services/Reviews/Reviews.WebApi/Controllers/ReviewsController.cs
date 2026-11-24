using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Commands.Reviews.AddComment;
using Reviews.Application.Commands.Reviews.CreateReview;
using Reviews.Application.Commands.Reviews.DeleteReview;
using Reviews.Application.Commands.Reviews.UpdateReview;
using Reviews.Application.Common.Models;
using Reviews.Application.Queries.Reviews.GetRestaurantRating;
using Reviews.Application.Queries.Reviews.GetReviewById;
using Reviews.Application.Queries.Reviews.GetReviewsList;

namespace Reviews.WebApi.Controllers;

/// <summary>Відгуки на страви, ресторани й кур'єрів.</summary>
[Route("api/reviews")]
public class ReviewsController : ApiControllerBase
{
    /// <summary>
    /// Список відгуків: фільтри, сортування, пагінація — усе в MongoDB ($match/$sort/$skip/$limit).
    /// Приклад: /api/reviews?restaurantId=1&amp;targetType=dish&amp;minRating=4&amp;sortBy=rating&amp;page=1&amp;pageSize=10
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReviewListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReviewListItemDto>>> GetList(
        [FromQuery] long? restaurantId, [FromQuery] long? dishId, [FromQuery] string? targetType,
        [FromQuery] int? minRating, [FromQuery] long? customerId, [FromQuery] string? search,
        [FromQuery] string sortBy = "createdAt", [FromQuery] string sortDir = "desc",
        [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var result = await Sender.Send(new GetReviewsListQuery(
            restaurantId, dishId, targetType, minRating, customerId, search, sortBy, sortDir, page, pageSize), ct);
        Response.Headers["X-Total-Count"] = result.TotalCount.ToString();
        return Ok(result);
    }

    /// <summary>Відгук за Id (рядковий ObjectId). Заголовок ETag — версія документа.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDetailsDto>> GetById(string id, CancellationToken ct)
    {
        var review = await Sender.Send(new GetReviewByIdQuery(id), ct);
        SetETag(review.Version);
        return Ok(review);
    }

    /// <summary>Середня оцінка ресторану з розбивкою за типами відгуків (aggregation pipeline).</summary>
    [HttpGet("restaurants/{restaurantId:long}/rating")]
    [ProducesResponseType<RestaurantRatingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RestaurantRatingDto>> GetRestaurantRating(long restaurantId, CancellationToken ct)
        => Ok(await Sender.Send(new GetRestaurantRatingQuery(restaurantId), ct));

    /// <summary>
    /// Залишити відгук → 201, Location: /api/reviews/{рядковий ObjectId}.
    /// Повторний відгук того ж клієнта на ту саму ціль → 400 (валідатор) або 409 (unique-індекс).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedResponse>> Create(CreateReviewCommand command, CancellationToken ct)
    {
        var id = await Sender.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new CreatedResponse(id));
    }

    /// <summary>
    /// Редагувати відгук. Очікувана версія — у заголовку If-Match (значення ETag з GET) або в тілі;
    /// якщо документ уже змінили — 409.
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDetailsDto>> Update(string id, UpdateReviewRequest request, CancellationToken ct)
    {
        var updated = await Sender.Send(new UpdateReviewCommand(
            id, request.Rating, request.Text, request.Aspects, IfMatchVersion() ?? request.ExpectedVersion), ct);
        SetETag(updated.Version);
        return Ok(updated);
    }

    /// <summary>Видалити відгук (If-Match — необов'язково).</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await Sender.Send(new DeleteReviewCommand(id, IfMatchVersion()), ct);
        return NoContent();
    }

    /// <summary>Додати коментар (вкладений масив comments).</summary>
    [HttpPost("{id}/comments")]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDetailsDto>> AddComment(string id, AddCommentRequest request, CancellationToken ct)
    {
        var review = await Sender.Send(new AddCommentCommand(id, request.AuthorName, request.Text), ct);
        SetETag(review.Version);
        return Ok(review);
    }
}

/// <summary>Тіло відповіді 201: рядковий ObjectId створеного документа.</summary>
public sealed record CreatedResponse(string Id);

public sealed record UpdateReviewRequest(int Rating, string? Text, AspectsDto? Aspects, int? ExpectedVersion);

public sealed record AddCommentRequest(string AuthorName, string Text);
