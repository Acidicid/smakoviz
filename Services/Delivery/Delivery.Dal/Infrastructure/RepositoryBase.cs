using System.Data;
using Microsoft.Data.SqlClient;

namespace Delivery.Dal.Infrastructure;

/// <summary>
/// Спільна основа всіх репозиторіїв. Репозиторій отримує НЕ рядок підключення,
/// а з'єднання Unit of Work і функцію доступу до поточної транзакції —
/// так усі репозиторії однієї операції працюють в одній транзакції.
/// </summary>
public abstract class RepositoryBase
{
    /// <summary>
    /// Таймаут команди, с. Процедури тримають UPDLOCK лише на рядок замовлення,
    /// тож 30 с з великим запасом; довше — ознака блокування, краще впасти, ніж «висіти».
    /// </summary>
    protected const int CommandTimeoutSeconds = 30;

    protected readonly SqlConnection Connection;
    private readonly Func<SqlTransaction?> _currentTransaction;

    protected RepositoryBase(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    {
        Connection = connection;
        _currentTransaction = currentTransaction;
    }

    /// <summary>Поточна транзакція Unit of Work (або null, якщо її не розпочато).</summary>
    protected SqlTransaction? Transaction => _currentTransaction();

    protected async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (Connection.State != ConnectionState.Open)
            await Connection.OpenAsync(ct);
    }

    /// <summary>Виконати дію й перекласти SqlException у доменний виняток (див. SqlErrorTranslator).</summary>
    protected static async Task<T> TranslateErrorsAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (SqlErrorTranslator.Translate(ex) is { } domainException)
        {
            throw domainException;
        }
    }

    protected static async Task TranslateErrorsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException ex) when (SqlErrorTranslator.Translate(ex) is { } domainException)
        {
            throw domainException;
        }
    }
}
