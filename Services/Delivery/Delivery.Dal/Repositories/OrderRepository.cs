using System.Data;
using Dapper;
using Delivery.Dal.Abstractions;
using Delivery.Dal.Infrastructure;
using Delivery.Domain.Models;
using Microsoft.Data.SqlClient;

namespace Delivery.Dal.Repositories;

/// <summary>
/// Замовлення доставки (ADO.NET + Dapper). Записи йдуть через збережувані процедури
/// з Лаб. 01 — там живуть правила життєвого циклу; читання — через Dapper
/// (QueryMultiple для процедури з трьома наборами, multi-mapping для JOIN).
/// </summary>
public sealed class OrderRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : BaseDapperRepository<DeliveryOrder>(connection, currentTransaction,
        tableName: "dbo.DeliveryOrders",
        insertColumns: ["OrderNumber", "CustomerId", "RestaurantId", "RestaurantName", "DeliveryFee", "CreatedBy"],
        softDelete: true),
      IOrderRepository
{
    /// <summary>
    /// Створення йде через usp_Order_Create: шапка + деталі доставки + перший запис історії
    /// в ОДНІЙ транзакції. Тому перевизначаємо узагальнений INSERT.
    /// </summary>
    public override async Task<long> AddAsync(DeliveryOrder order, CancellationToken ct = default)
    {
        var details = order.Details
            ?? throw new ArgumentException("Order must have delivery details", nameof(order));

        var p = new DynamicParameters();
        p.Add("@OrderNumber", order.OrderNumber, DbType.String, size: 20);
        p.Add("@CustomerId", order.CustomerId, DbType.Int64);
        p.Add("@RestaurantId", order.RestaurantId, DbType.Int64);
        p.Add("@RestaurantName", order.RestaurantName, DbType.String, size: 150);
        p.Add("@Address", details.Address, DbType.String, size: 300);
        p.Add("@ContactPhone", details.ContactPhone, DbType.String, size: 20);
        p.Add("@CourierNote", details.CourierNote, DbType.String, size: 500);
        p.Add("@DesiredTime", details.DesiredTime, DbType.DateTime2);
        p.Add("@DeliveryFee", order.DeliveryFee, DbType.Decimal, precision: 10, scale: 2);
        p.Add("@CreatedBy", order.CreatedBy, DbType.String, size: 100);
        p.Add("@OrderId", dbType: DbType.Int64, direction: ParameterDirection.Output);

        await EnsureOpenAsync(ct);
        await TranslateErrorsAsync(() => Connection.ExecuteAsync(
            Command("dbo.usp_Order_Create", p, ct, CommandType.StoredProcedure)));

        return p.Get<long>("@OrderId");
    }

    /// <summary>М'яке видалення через usp_Order_SoftDelete (лише Delivered / Cancelled).</summary>
    public override async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await TranslateErrorsAsync(() => Connection.ExecuteAsync(
            Command("dbo.usp_Order_SoftDelete", new { OrderId = id }, ct, CommandType.StoredProcedure)));
        return true; // відсутнє замовлення процедура повідомляє через THROW 50001 → NotFoundException
    }

    public async Task<DeliveryOrder?> GetFullAsync(long orderId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        try
        {
            // процедура повертає 3 набори: шапка+деталі, позиції, історія
            using var grid = await Connection.QueryMultipleAsync(
                Command("dbo.usp_Order_GetById", new { OrderId = orderId }, ct, CommandType.StoredProcedure));

            var header = await grid.ReadSingleAsync<OrderHeaderRow>();
            var lines = (await grid.ReadAsync<OrderLine>()).AsList();
            var history = (await grid.ReadAsync<OrderStatusChange>()).AsList();

            var order = header.ToOrder();
            lines.ForEach(l => l.OrderId = order.Id);
            order.Lines = lines;
            order.History = history;
            return order;
        }
        catch (SqlException ex) when (ex.Number == 50001)
        {
            return null; // «не знайдено» — не помилка для читання; рішення приймає BLL
        }
    }

    public async Task<DeliveryOrder?> GetWithLinesAsync(long orderId, CancellationToken ct = default)
    {
        // Multi-mapping: один рядок JOIN = замовлення + ОДНА позиція.
        // splitOn: "DishId" — з цього стовпця починаються поля OrderLine.
        const string sql = """
            SELECT o.Id, o.OrderNumber, o.CustomerId, o.RestaurantId, o.RestaurantName, o.Status,
                   o.PlacedAt, o.ItemsTotal, o.DeliveryFee, o.TotalAmount, o.RowVersion,
                   l.DishId, l.DishName, l.UnitPrice, l.Quantity, l.Comment
            FROM dbo.DeliveryOrders o
            LEFT JOIN dbo.OrderLines l ON l.OrderId = o.Id
            WHERE o.Id = @OrderId AND o.IsDeleted = 0
            ORDER BY l.DishName
            """;

        await EnsureOpenAsync(ct);
        DeliveryOrder? result = null;
        await Connection.QueryAsync<DeliveryOrder, OrderLine?, DeliveryOrder>(
            Command(sql, new { OrderId = orderId }, ct),
            (order, line) =>
            {
                result ??= order;
                if (line is not null)   // LEFT JOIN без позицій → line == null
                {
                    line.OrderId = result.Id;
                    result.Lines.Add(line);
                }
                return result;
            },
            splitOn: "DishId");

        return result;
    }

    public async Task<long?> FindIdByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
    {
        // UNIQUE-індекс UQ_DeliveryOrders_OrderNumber
        const string sql = "SELECT Id FROM dbo.DeliveryOrders WHERE OrderNumber = @OrderNumber";
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteScalarAsync<long?>(Command(sql, new { OrderNumber = orderNumber }, ct));
    }

    public async Task<PagedResult<DeliveryOrder>> SearchAsync(long? customerId, string? status, int page, int pageSize,
        CancellationToken ct = default)
    {
        // Для «мої замовлення» (CustomerId + Status) працює IX_DeliveryOrders_Customer_Status.
        // Фільтри необов'язкові: (@X IS NULL OR Col = @X) — значення лише параметрами.
        const string sql = """
            SELECT Id, OrderNumber, CustomerId, RestaurantId, RestaurantName, Status, PlacedAt,
                   ItemsTotal, DeliveryFee, TotalAmount, CreatedAt, UpdatedAt, RowVersion,
                   COUNT(*) OVER() AS TotalCount
            FROM dbo.DeliveryOrders
            WHERE IsDeleted = 0
              AND (@CustomerId IS NULL OR CustomerId = @CustomerId)
              AND (@Status IS NULL OR Status = @Status)
            ORDER BY PlacedAt DESC, Id DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            """;

        await EnsureOpenAsync(ct);
        var rows = (await Connection.QueryAsync<OrderSearchRow>(Command(sql, new
        {
            CustomerId = customerId,
            Status = status,
            Skip = (page - 1) * pageSize,
            Take = pageSize
        }, ct))).AsList();

        var total = rows.Count > 0 ? rows[0].TotalCount : 0;
        return new PagedResult<DeliveryOrder>(rows.Cast<DeliveryOrder>().ToList(), page, pageSize, total);
    }

    public async Task AddLineAsync(long orderId, long dishId, int quantity, string? comment, string? changedBy,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await TranslateErrorsAsync(() => Connection.ExecuteAsync(Command("dbo.usp_Order_AddLine", new
        {
            OrderId = orderId,
            DishId = dishId,
            Quantity = quantity,
            Comment = comment,
            ChangedBy = changedBy
        }, ct, CommandType.StoredProcedure)));
    }

    public async Task ChangeStatusAsync(long orderId, string newStatus, string? changedBy, string? reason,
        byte[]? expectedRowVersion, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("@OrderId", orderId, DbType.Int64);
        p.Add("@NewStatus", newStatus, DbType.String, size: 20);
        p.Add("@ChangedBy", changedBy, DbType.String, size: 100);
        p.Add("@Reason", reason, DbType.String, size: 300);
        p.Add("@ExpectedRowVersion", expectedRowVersion, DbType.Binary, size: 8);

        await EnsureOpenAsync(ct);
        // 50001 → 404, 50002 (недопустимий перехід) → 409, 50003 (RowVersion) → 409, 50004 → 422
        await TranslateErrorsAsync(() => Connection.ExecuteAsync(
            Command("dbo.usp_Order_ChangeStatus", p, ct, CommandType.StoredProcedure)));
    }

    // ---------- рядки результатів, що не збігаються 1:1 з моделлю ----------

    private sealed class OrderSearchRow : DeliveryOrder
    {
        public int TotalCount { get; set; }
    }

    /// <summary>Перший набір usp_Order_GetById: колонки замовлення + деталі доставки.</summary>
    private sealed class OrderHeaderRow
    {
        public long Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public long CustomerId { get; set; }
        public long RestaurantId { get; set; }
        public string RestaurantName { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime PlacedAt { get; set; }
        public decimal ItemsTotal { get; set; }
        public decimal DeliveryFee { get; set; }
        public decimal TotalAmount { get; set; }
        public byte[] RowVersion { get; set; } = [];
        public string? Address { get; set; }
        public string? ContactPhone { get; set; }
        public string? CourierNote { get; set; }
        public DateTime? DesiredTime { get; set; }

        public DeliveryOrder ToOrder() => new()
        {
            Id = Id, OrderNumber = OrderNumber, CustomerId = CustomerId,
            RestaurantId = RestaurantId, RestaurantName = RestaurantName, Status = Status,
            PlacedAt = PlacedAt, ItemsTotal = ItemsTotal, DeliveryFee = DeliveryFee,
            TotalAmount = TotalAmount, RowVersion = RowVersion,
            Details = Address is null ? null : new DeliveryDetails
            {
                OrderId = Id, Address = Address, ContactPhone = ContactPhone ?? "",
                CourierNote = CourierNote, DesiredTime = DesiredTime
            }
        };
    }
}
