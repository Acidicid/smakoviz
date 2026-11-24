using Delivery.Domain.Models;

namespace Delivery.Dal.Abstractions;

/// <summary>
/// Клієнти — репозиторій на ЧИСТОМУ ADO.NET. Свідомо НЕ успадковує IGenericRepository:
/// показує ручну роботу з SqlCommand, параметрами, SqlDataReader і транзакцією
/// без сторонніх бібліотек, а набір операцій у нього свій (пошук за email, update з RowVersion).
/// </summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<PagedResult<Customer>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct = default);
    Task<bool> ExistsAsync(long id, CancellationToken ct = default);
    Task<long> AddAsync(Customer customer, CancellationToken ct = default);

    /// <summary>Оновлення з перевіркою RowVersion (оптимістична конкурентність).</summary>
    Task UpdateAsync(Customer customer, byte[] expectedRowVersion, CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(long id, string? changedBy, CancellationToken ct = default);
}

/// <summary>Локальна копія страв з Меню — Dapper.</summary>
public interface IDishRepository : IGenericRepository<Dish>
{
    Task<IReadOnlyList<Dish>> GetByRestaurantAsync(long restaurantId, bool onlyAvailable, CancellationToken ct = default);
    Task<IReadOnlyList<Dish>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Синхронізація копії з Меню: вставити або оновити (MERGE).</summary>
    Task UpsertAsync(Dish dish, CancellationToken ct = default);
}

/// <summary>Замовлення — Dapper + збережувані процедури з Лаб. 01.</summary>
public interface IOrderRepository : IGenericRepository<DeliveryOrder>
{
    /// <summary>Повне замовлення: шапка + деталі + позиції + історія (usp_Order_GetById, QueryMultiple).</summary>
    Task<DeliveryOrder?> GetFullAsync(long orderId, CancellationToken ct = default);

    /// <summary>Шапка + позиції одним JOIN-запитом (Dapper multi-mapping).</summary>
    Task<DeliveryOrder?> GetWithLinesAsync(long orderId, CancellationToken ct = default);

    /// <summary>Id замовлення за номером (для idempotency-ключа) або null.</summary>
    Task<long?> FindIdByOrderNumberAsync(string orderNumber, CancellationToken ct = default);

    Task<PagedResult<DeliveryOrder>> SearchAsync(long? customerId, string? status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>usp_Order_AddLine: знімок назви й ціни, перерахунок суми.</summary>
    Task AddLineAsync(long orderId, long dishId, int quantity, string? comment, string? changedBy, CancellationToken ct = default);

    /// <summary>usp_Order_ChangeStatus: транзакційна зміна статусу з перевіркою переходу.</summary>
    Task ChangeStatusAsync(long orderId, string newStatus, string? changedBy, string? reason,
        byte[]? expectedRowVersion, CancellationToken ct = default);
}

/// <summary>
/// Unit of Work: ОДНЕ з'єднання й ОДНА транзакція на бізнес-операцію.
/// Усі репозиторії отримують те саме з'єднання і доступ до поточної транзакції,
/// тому або зберігаються зміни всіх репозиторіїв, або жодного.
/// Реєструється в DI як Scoped — один екземпляр на HTTP-запит.
/// </summary>
/// <summary>Оброблені інтеграційні події (Лаб. 08): ідемпотентність споживача.</summary>
public interface IProcessedEventRepository
{
    Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>false — подію вже оброблено (порушення PRIMARY KEY).</summary>
    Task<bool> TryMarkProcessedAsync(Guid eventId, string eventType, string consumer, CancellationToken ct = default);
}

public interface IUnitOfWork : IAsyncDisposable
{
    ICustomerRepository Customers { get; }
    IDishRepository Dishes { get; }
    IOrderRepository Orders { get; }
    IProcessedEventRepository ProcessedEvents { get; }

    bool HasActiveTransaction { get; }

    Task BeginTransactionAsync(System.Data.IsolationLevel level = System.Data.IsolationLevel.ReadCommitted,
        CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
