using System.Data;
using Delivery.Dal.Abstractions;
using Delivery.Dal.Repositories;
using Microsoft.Data.SqlClient;

namespace Delivery.Dal.Infrastructure;

/// <summary>
/// Unit of Work на ADO.NET: одне з'єднання на HTTP-запит (Scoped), одна транзакція на бізнес-операцію.
///
/// Як гарантується «все або нічого»: усі репозиторії створюються тут і отримують ТЕ САМЕ
/// з'єднання та функцію () => _transaction. Коли сервіс викликає BeginTransactionAsync,
/// кожна наступна команда будь-якого репозиторію автоматично йде в цю транзакцію.
/// Якби репозиторій мав власне з'єднання/транзакцію, його зміни не відкотилися б разом з іншими.
///
/// Рівень ізоляції за замовчуванням — READ COMMITTED (як і в SQL Server):
///   + не читаємо незакомічені дані (dirty reads), мінімум блокувань;
///   − можливі неповторювані читання між двома SELECT в одній транзакції.
/// Чому цього досить: критичні рядки (замовлення під час зміни статусу) процедури Лаб. 01
/// блокують точково (UPDLOCK), а паралельні правки ловить RowVersion.
/// SERIALIZABLE дав би менше аномалій, але більше блокувань і ризик взаємних блокувань (deadlocks).
/// Таймаут кожної команди — 30 с (RepositoryBase.CommandTimeoutSeconds).
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;

    public UnitOfWork(string connectionString)
    {
        // З'єднання береться з пулу SqlClient (пул увімкнений за замовчуванням),
        // а при Dispose повертається в пул — фізично не закривається.
        _connection = new SqlConnection(connectionString);

        Customers = new CustomerRepository(_connection, () => _transaction);   // чистий ADO.NET
        Dishes = new DishRepository(_connection, () => _transaction);          // Dapper
        Orders = new OrderRepository(_connection, () => _transaction);         // Dapper + процедури
    }

    public ICustomerRepository Customers { get; }
    public IDishRepository Dishes { get; }
    public IOrderRepository Orders { get; }

    public bool HasActiveTransaction => _transaction is not null;

    public async Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Transaction is already started");

        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);

        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(level, ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to commit");

        try
        {
            await _transaction.CommitAsync(ct);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;

        try
        {
            // Якщо процедура впала з XACT_ABORT ON, SQL Server уже сам відкотив транзакцію:
            // тоді SqlTransaction «завершена» (Connection == null) і Rollback кинув би виняток.
            if (_transaction.Connection is not null)
                await _transaction.RollbackAsync(ct);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // незавершена транзакція при Dispose відкочується автоматично
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
