using Grpc.Core;
using Menu.Bll.Dtos;
using Menu.Bll.Services;
using Menu.Domain.Exceptions;

namespace Menu.Grpc;

/// <summary>
/// gRPC-сервіс «Меню» — лише АДАПТЕР над BLL (як контролер): валідує вхід, викликає ті самі сервіси
/// (через кешовані декоратори — кеш спільний для REST і gRPC), перетворює DTO у protobuf-повідомлення,
/// а доменні винятки — у RpcException з правильним кодом (NotFound / InvalidArgument / FailedPrecondition),
/// а не загальний Internal.
/// </summary>
public sealed class MenuGrpcService(
    IRestaurantService restaurants,
    IDishService dishes,
    ILogger<MenuGrpcService> logger) : MenuService.MenuServiceBase
{
    private const int MaxIdsPerRequest = 100;

    public override async Task<RestaurantReply> GetRestaurant(GetRestaurantRequest request, ServerCallContext context)
    {
        EnsurePositive(request.Id, "id");
        var restaurant = await Execute(() => restaurants.GetByIdAsync(request.Id, request.OnlyAvailable, context.CancellationToken));
        return ToReply(restaurant);
    }

    public override async Task<DishReply> GetDish(GetDishRequest request, ServerCallContext context)
    {
        EnsurePositive(request.Id, "id");
        var dish = await Execute(() => dishes.GetByIdAsync(request.Id, context.CancellationToken));
        return ToReply(dish);
    }

    public override async Task<DishesReply> GetDishesByIds(GetDishesByIdsRequest request, ServerCallContext context)
    {
        if (request.Ids.Count == 0 || request.Ids.Count > MaxIdsPerRequest)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"ids: from 1 to {MaxIdsPerRequest} values expected"));
        foreach (var id in request.Ids) EnsurePositive(id, "ids");

        var reply = new DishesReply();
        foreach (var id in request.Ids.Distinct())
        {
            try
            {
                reply.Dishes.Add(ToReply(await dishes.GetByIdAsync(id, context.CancellationToken)));   // кожна — з кешу
            }
            catch (EntityNotFoundException)
            {
                logger.LogDebug("Dish {DishId} not found — skipped in GetDishesByIds", id);
            }
        }
        return reply;
    }

    // ------------------------------------------------------------------ винятки → RpcException

    private static void EnsurePositive(long id, string field)
    {
        if (id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"{field} must be positive"));
    }

    private static async Task<T> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (EntityNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                string.Join("; ", ex.Errors.Select(e => $"{e.Key}: {string.Join(", ", e.Value)}"))));
        }
        catch (FluentValidation.ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (DomainException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    // ------------------------------------------------------------------ DTO → protobuf

    private static DecimalValue Money(decimal value)
    {
        var units = decimal.ToInt64(decimal.Truncate(value));
        return new DecimalValue { Units = units, Nanos = decimal.ToInt32((value - units) * 1_000_000_000m) };
    }

    private static RestaurantReply ToReply(RestaurantDetailsDto r)
    {
        var reply = new RestaurantReply
        {
            Id = r.Id,
            Name = r.Name,
            City = r.City,
            CuisineType = r.CuisineType,
            IsActive = r.IsActive,
            Profile = r.Profile is null ? null : new RestaurantProfile
            {
                Address = r.Profile.Address,
                Phone = r.Profile.Phone,
                OpensAt = r.Profile.OpensAt.ToString("HH:mm"),
                ClosesAt = r.Profile.ClosesAt.ToString("HH:mm"),
                MinOrderAmount = Money(r.Profile.MinOrderAmount)
            }
        };
        reply.Dishes.AddRange(r.Dishes.Select(d => new DishSummary
        {
            Id = d.Id,
            Name = d.Name,
            Price = Money(d.Price),
            IsAvailable = d.IsAvailable,
            AvailablePortions = d.AvailablePortions
        }));
        return reply;
    }

    private static DishReply ToReply(DishDto d) => new()
    {
        Id = d.Id,
        RestaurantId = d.RestaurantId,
        RestaurantName = d.RestaurantName,
        Name = d.Name,
        Price = Money(d.Price),
        IsAvailable = d.IsAvailable,
        AvailablePortions = d.AvailablePortions
    };
}
