using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using Delivery.Bll.Dtos;
using Delivery.Dal.Abstractions;
using Delivery.Domain.Exceptions;
using Delivery.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Delivery.Bll.Services;

public sealed class OrderService(IUnitOfWork uow, IMapper mapper, ILogger<OrderService> logger) : IOrderService
{
    /// <summary>
    /// Власні spans бізнес-операцій (Лаб. 05). Ім'я джерела підключене в ServiceDefaults через AddSource("SmakoViz.*"),
    /// тож у Dashboard → Traces видно «PlaceOrder» / «ChangeOrderStatus» над SQL-spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("SmakoViz.Delivery");

    /// <summary>Бізнес-правило: доставка безкоштовна від цієї суми страв.</summary>
    public const decimal FreeDeliveryThreshold = 500m;

    /// <summary>Вартість доставки, якщо сума менша за поріг.</summary>
    public const decimal StandardDeliveryFee = 40m;

    public async Task<OrderDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var order = await uow.Orders.GetFullAsync(id, ct)
            ?? throw new NotFoundException($"Order {id} not found");
        return mapper.Map<OrderDto>(order);
    }

    public async Task<PagedResult<OrderSummaryDto>> SearchAsync(long? customerId, string? status, int page, int pageSize,
        CancellationToken ct = default)
    {
        ServiceGuards.Paging(page, pageSize);
        if (status is not null && !OrderStatus.IsKnown(status))
            throw UnknownStatus(status);

        var result = await uow.Orders.SearchAsync(customerId, status, page, pageSize, ct);
        return new PagedResult<OrderSummaryDto>(
            mapper.Map<List<OrderSummaryDto>>(result.Items), result.Page, result.PageSize, result.TotalCount);
    }

    /// <summary>
    /// ТРАНЗАКЦІЙНА бізнес-операція: оформлення замовлення.
    /// В одній транзакції працюють три репозиторії:
    ///   Customers (ADO.NET) — чи існує клієнт;
    ///   Dishes    (Dapper)  — ціни з локальної копії Меню, усі страви з одного ресторану;
    ///   Orders    (Dapper)  — usp_Order_Create (шапка + деталі + історія), потім usp_Order_AddLine для кожної страви.
    /// Якщо будь-який крок впаде (наприклад, страва стала недоступною — THROW 50004 у usp_Order_AddLine),
    /// відкочується ВСЕ: замовлення, деталі доставки, вже додані позиції.
    /// </summary>
    public async Task<PlaceOrderResult> PlaceOrderAsync(PlaceOrderDto input, string? idempotencyKey,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("PlaceOrder");
        activity?.SetTag("customer.id", input.CustomerId);
        activity?.SetTag("order.items_count", input.Items.Count);

        var orderNumber = idempotencyKey is null ? NewOrderNumber() : OrderNumberFromKey(idempotencyKey);

        // Ідемпотентність: повтор того самого запиту (клієнт не дочекався відповіді й надіслав ще раз)
        // не створює друге замовлення, а повертає перше.
        if (idempotencyKey is not null && await uow.Orders.FindIdByOrderNumberAsync(orderNumber, ct) is { } existingId)
            return new PlaceOrderResult(await GetByIdAsync(existingId, ct), Created: false);

        long orderId;
        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // 1. Бізнес-валідація: клієнт існує (репозиторій №1 — ADO.NET)
            if (!await uow.Customers.ExistsAsync(input.CustomerId, ct))
                throw new ValidationException("Customer does not exist",
                    new Dictionary<string, string[]> { ["customerId"] = [$"Customer {input.CustomerId} not found"] });

            // 2. Страви з локальної копії Меню (репозиторій №2 — Dapper)
            var dishIds = input.Items.Select(i => i.DishId).Distinct().ToList();
            var dishes = (await uow.Dishes.GetByIdsAsync(dishIds, ct)).ToDictionary(d => d.Id);

            var missing = dishIds.Where(id => !dishes.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                throw new ValidationException("Some dishes do not exist",
                    new Dictionary<string, string[]> { ["items"] = [$"Unknown dish ids: {string.Join(", ", missing)}"] });

            var restaurantIds = dishes.Values.Select(d => d.RestaurantId).Distinct().ToList();
            if (restaurantIds.Count > 1)
                throw new BusinessRuleException("All dishes in one order must be from the same restaurant");

            // 3. Вартість доставки — бізнес-правило BLL (ціни — з копії Меню, а не від клієнта)
            var itemsTotal = input.Items.Sum(i => dishes[i.DishId].Price * i.Quantity);
            var deliveryFee = itemsTotal >= FreeDeliveryThreshold ? 0m : StandardDeliveryFee;

            // 4. Замовлення + деталі доставки + історія (репозиторій №3 — Dapper + usp_Order_Create)
            orderId = await uow.Orders.AddAsync(new DeliveryOrder
            {
                OrderNumber = orderNumber,
                CustomerId = input.CustomerId,
                RestaurantId = restaurantIds[0],
                RestaurantName = input.RestaurantName.Trim(),
                DeliveryFee = deliveryFee,
                CreatedBy = ServiceGuards.CurrentUser,
                Details = new DeliveryDetails
                {
                    Address = input.Address.Trim(),
                    ContactPhone = input.ContactPhone,
                    CourierNote = input.CourierNote,
                    DesiredTime = input.DesiredTime
                }
            }, ct);

            // 5. Позиції: процедура сама бере назву й ціну з копії і записує їх як ЗНІМОК,
            //    а також ще раз перевіряє доступність — це остаточна перевірка під блокуванням
            //    (страву могли зняти з продажу між кроком 2 і цим моментом).
            foreach (var item in input.Items)
                await uow.Orders.AddLineAsync(orderId, item.DishId, item.Quantity, item.Comment,
                    ServiceGuards.CurrentUser, ct);

            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);   // замовлення і вже додані позиції зникають
            throw;
        }

        var placed = await GetByIdAsync(orderId, ct);
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("order.status", placed.Status);
        // Бізнес-подія: лише ідентифікатори й суми — без адреси й телефону (персональні дані)
        logger.LogInformation(
            "Order {OrderId} ({OrderNumber}) placed by customer {CustomerId}: {ItemsCount} items, total {TotalAmount}, status {Status}",
            orderId, placed.OrderNumber, placed.CustomerId, placed.Lines.Count, placed.TotalAmount, placed.Status);

        return new PlaceOrderResult(placed, Created: true);
    }

    public async Task<OrderDto> AddLineAsync(long orderId, AddOrderLineDto input, CancellationToken ct = default)
    {
        // usp_Order_AddLine — сама по собі транзакція: позиція + перерахунок суми.
        // 50001 → 404, 50004 (не Placed / недоступна / інший ресторан) → 422
        await uow.Orders.AddLineAsync(orderId, input.DishId, input.Quantity, input.Comment,
            ServiceGuards.CurrentUser, ct);
        return await GetByIdAsync(orderId, ct);
    }

    public async Task ConfirmAsync(long orderId, CancellationToken ct = default)
    {
        var order = await uow.Orders.GetWithLinesAsync(orderId, ct)
            ?? throw new NotFoundException($"Order {orderId} not found");

        if (order.Lines.Count == 0)
            throw new BusinessRuleException($"Order {orderId} has no dishes and cannot be confirmed");

        // Допустимість переходу перевіряє процедура: повторне підтвердження → 50002 → 409.
        // RowVersion, прочитаний вище, захищає від зміни замовлення між читанням і записом.
        await uow.Orders.ChangeStatusAsync(orderId, OrderStatus.Confirmed, ServiceGuards.CurrentUser,
            "Confirmed by restaurant", order.RowVersion, ct);

        logger.LogInformation("Order {OrderId} confirmed, {ItemsCount} items, status {Status}",
            orderId, order.Lines.Count, OrderStatus.Confirmed);
    }

    public Task CancelAsync(long orderId, string? reason, CancellationToken ct = default) =>
        // Cancelled дозволено лише з Placed/Confirmed — інакше процедура кидає 50002 → 409
        uow.Orders.ChangeStatusAsync(orderId, OrderStatus.Cancelled, ServiceGuards.CurrentUser,
            reason ?? "Cancelled by customer", null, ct);

    public async Task ChangeStatusAsync(long orderId, ChangeOrderStatusDto input, CancellationToken ct = default)
    {
        if (!OrderStatus.IsKnown(input.Status))
            throw UnknownStatus(input.Status);

        using var activity = ActivitySource.StartActivity("ChangeOrderStatus");
        activity?.SetTag("order.id", orderId);
        activity?.SetTag("order.status", input.Status);

        var rowVersion = input.RowVersion is null ? null : ServiceGuards.RowVersion(input.RowVersion);

        await uow.Orders.ChangeStatusAsync(orderId, input.Status, ServiceGuards.CurrentUser,
            input.Reason, rowVersion, ct);

        logger.LogInformation("Order {OrderId} status changed to {Status}", orderId, input.Status);
    }

    public async Task DeleteAsync(long orderId, CancellationToken ct = default) =>
        // лише Delivered/Cancelled; активне замовлення → 50004 → 422
        await uow.Orders.DeleteAsync(orderId, ct);

    // ---------------- допоміжні ----------------

    private static ValidationException UnknownStatus(string status) =>
        new("Unknown order status", new Dictionary<string, string[]>
        {
            ["status"] = [$"'{status}' is not a valid status. Allowed: {string.Join(", ", OrderStatus.All)}"]
        });

    /// <summary>SV-yyMMddHHmmss-NNN (18 символів ≤ NVARCHAR(20)).</summary>
    private static string NewOrderNumber() =>
        $"SV-{DateTime.UtcNow:yyMMddHHmmss}{RandomNumberGenerator.GetInt32(0, 1000):D3}";

    /// <summary>Детермінований номер з Idempotency-Key: той самий ключ → той самий номер.</summary>
    private static string OrderNumberFromKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()));
        return "SV-K" + Convert.ToHexString(hash)[..14];   // 18 символів
    }
}
