using FluentValidation;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MediatR;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Queries.Reviews.GetRestaurantRating;
using Reviews.Application.Queries.Reviews.GetReviewsList;
using Reviews.Domain.Exceptions;

namespace Reviews.Grpc;

/// <summary>
/// gRPC-адаптер «Відгуків»: як і контролери, лише надсилає query через MediatR (ISender) — тож працюють ті самі
/// pipeline behaviors (логування, валідація, КЕШ). Винятки → RpcException з правильним кодом.
/// </summary>
public sealed class ReviewsGrpcService(ISender sender) : ReviewsService.ReviewsServiceBase
{
    public override async Task<RestaurantRatingReply> GetRestaurantRating(GetRestaurantRatingRequest request, ServerCallContext context)
    {
        if (request.RestaurantId <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "restaurant_id must be positive"));

        var rating = await Execute(() => sender.Send(new GetRestaurantRatingQuery(request.RestaurantId), context.CancellationToken));
        return new RestaurantRatingReply
        {
            RestaurantId = rating.RestaurantId,
            ReviewsCount = rating.ReviewsCount,
            AverageRating = rating.AverageRating
        };
    }

    public override async Task<ReviewsReply> ListReviews(ListReviewsRequest request, ServerCallContext context)
    {
        if (request.RestaurantId <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "restaurant_id must be positive"));
        if (request.PageSize is < 1 or > 50)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "page_size must be between 1 and 50"));

        var page = await Execute(() => sender.Send(new GetReviewsListQuery(
            request.RestaurantId, null, null, null, request.HasCustomerId ? request.CustomerId : null, null,
            "createdAt", "desc", 1, request.PageSize), context.CancellationToken));

        var reply = new ReviewsReply { TotalCount = page.TotalCount };
        reply.Reviews.AddRange(page.Items.Select(r =>
        {
            var review = new Review
            {
                Id = r.Id,
                TargetType = r.TargetType,
                RestaurantId = r.RestaurantId,
                AuthorName = r.AuthorName,
                Rating = r.Rating,
                CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc))
            };
            if (r.DishId is { } dishId) review.DishId = dishId;          // optional-поля: лише якщо є значення
            if (r.DishName is { } dishName) review.DishName = dishName;
            if (r.Text is { } text) review.Text = text;
            return review;
        }));
        return reply;
    }

    private static async Task<T> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                string.Join("; ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))));
        }
        catch (DomainException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (ConflictException ex)
        {
            throw new RpcException(new Status(StatusCode.Aborted, ex.Message));
        }
    }
}
