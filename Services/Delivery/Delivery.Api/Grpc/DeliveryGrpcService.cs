using Delivery.Bll.Dtos;
using Delivery.Bll.Services;
using Delivery.Domain.Exceptions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Delivery.Grpc;

/// <summary>
/// gRPC-адаптер над BLL «Доставки»: ті самі сервіси, що й у контролерів (з кешем), доменні винятки →
/// RpcException з правильним кодом: NotFound, InvalidArgument, FailedPrecondition (бізнес-правило), Aborted (конкурентність).
/// </summary>
public sealed class DeliveryGrpcService(IOrderService orders) : DeliveryService.DeliveryServiceBase
{
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be positive"));

        var order = await Execute(() => orders.GetByIdAsync(request.Id, context.CancellationToken));
        return ToReply(order);
    }

    public override async Task<OrderSummariesReply> GetCustomerOrders(GetCustomerOrdersRequest request, ServerCallContext context)
    {
        if (request.CustomerId <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "customer_id must be positive"));
        if (request.Take is < 0 or > 50)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "take must be between 1 and 50"));

        var page = await Execute(() => orders.SearchAsync(request.CustomerId, null, 1,
            request.Take == 0 ? 10 : request.Take, context.CancellationToken));

        var reply = new OrderSummariesReply { TotalCount = page.TotalCount };
        reply.Orders.AddRange(page.Items.Select(o => new OrderSummary
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            RestaurantName = o.RestaurantName,
            Status = o.Status,
            PlacedAt = ToTimestamp(o.PlacedAt),
            TotalAmount = Money(o.TotalAmount)
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
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (ConcurrencyConflictException ex)
        {
            throw new RpcException(new Status(StatusCode.Aborted, ex.Message));
        }
        catch (BusinessConflictException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    private static DecimalValue Money(decimal value)
    {
        var units = decimal.ToInt64(decimal.Truncate(value));
        return new DecimalValue { Units = units, Nanos = decimal.ToInt32((value - units) * 1_000_000_000m) };
    }

    /// <summary>У БД час зберігається в UTC (SYSUTCDATETIME), Dapper повертає Kind=Unspecified — позначаємо явно.</summary>
    private static Timestamp ToTimestamp(DateTime value)
        => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static OrderReply ToReply(OrderDto o)
    {
        var reply = new OrderReply
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            CustomerId = o.CustomerId,
            RestaurantId = o.RestaurantId,
            RestaurantName = o.RestaurantName,
            Status = o.Status,
            PlacedAt = ToTimestamp(o.PlacedAt),
            ItemsTotal = Money(o.ItemsTotal),
            DeliveryFee = Money(o.DeliveryFee),
            TotalAmount = Money(o.TotalAmount)
        };
        reply.Lines.AddRange(o.Lines.Select(l => new OrderLine
        {
            DishId = l.DishId,
            DishName = l.DishName,
            UnitPrice = Money(l.UnitPrice),
            Quantity = l.Quantity,
            LineTotal = Money(l.LineTotal)
        }));
        return reply;
    }
}
