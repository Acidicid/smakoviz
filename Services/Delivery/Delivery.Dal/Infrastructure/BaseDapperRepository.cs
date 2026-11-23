using Dapper;
using Delivery.Dal.Abstractions;
using Microsoft.Data.SqlClient;

namespace Delivery.Dal.Infrastructure;

/// <summary>
/// Узагальнений CRUD на Dapper — написаний ОДИН раз для всіх Dapper-репозиторіїв.
/// Конкретні репозиторії передають ім'я таблиці й список колонок для INSERT
/// і додають лише свої специфічні запити.
///
/// Безпека: ім'я таблиці й колонок — КОНСТАНТИ з коду (задаються в конструкторі
/// конкретного репозиторію), а не дані користувача, тому їх підстановка в текст
/// запиту безпечна. Усі ЗНАЧЕННЯ передаються лише параметрами (@Id тощо).
/// </summary>
public abstract class BaseDapperRepository<T> : RepositoryBase, IGenericRepository<T> where T : class
{
    protected readonly string TableName;
    private readonly string[] _insertColumns;
    private readonly bool _softDelete;

    protected BaseDapperRepository(
        SqlConnection connection,
        Func<SqlTransaction?> currentTransaction,
        string tableName,
        string[] insertColumns,
        bool softDelete)
        : base(connection, currentTransaction)
    {
        TableName = tableName;
        _insertColumns = insertColumns;
        _softDelete = softDelete;
    }

    /// <summary>Фільтр «живих» рядків для таблиць з м'яким видаленням.</summary>
    protected string NotDeleted => _softDelete ? " AND IsDeleted = 0" : "";

    /// <summary>CommandDefinition з поточною транзакцією UoW, таймаутом і токеном скасування.</summary>
    protected CommandDefinition Command(string sql, object? parameters, CancellationToken ct,
        System.Data.CommandType commandType = System.Data.CommandType.Text) =>
        new(sql, parameters, Transaction, CommandTimeoutSeconds, commandType, cancellationToken: ct);

    public virtual async Task<T?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {TableName} WHERE Id = @Id{NotDeleted}";
        await EnsureOpenAsync(ct);
        return await Connection.QuerySingleOrDefaultAsync<T>(Command(sql, new { Id = id }, ct));
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT * FROM {TableName} WHERE 1 = 1{NotDeleted} ORDER BY Id";
        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<T>(Command(sql, null, ct));
        return rows.AsList();
    }

    /// <summary>
    /// INSERT ... OUTPUT INSERTED.Id. Значення беруться з однойменних властивостей сутності
    /// (Dapper сам зробить з них параметри @Name, @Price ...).
    /// </summary>
    public virtual async Task<long> AddAsync(T entity, CancellationToken ct = default)
    {
        var columns = string.Join(", ", _insertColumns);
        var values = string.Join(", ", _insertColumns.Select(c => "@" + c));
        var sql = $"INSERT INTO {TableName} ({columns}) OUTPUT INSERTED.Id VALUES ({values})";

        await EnsureOpenAsync(ct);
        return await TranslateErrorsAsync(() =>
            Connection.ExecuteScalarAsync<long>(Command(sql, entity, ct)));
    }

    /// <summary>Для таблиць з IsDeleted — м'яке видалення, для інших — фізичне.</summary>
    public virtual async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var sql = _softDelete
            ? $"UPDATE {TableName} SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id AND IsDeleted = 0"
            : $"DELETE FROM {TableName} WHERE Id = @Id";

        await EnsureOpenAsync(ct);
        var affected = await TranslateErrorsAsync(() =>
            Connection.ExecuteAsync(Command(sql, new { Id = id }, ct)));
        return affected > 0;
    }
}
