using System.Data;
using Delivery.Dal.Abstractions;
using Delivery.Dal.Infrastructure;
using Delivery.Domain.Exceptions;
using Delivery.Domain.Models;
using Microsoft.Data.SqlClient;

namespace Delivery.Dal.Repositories;

/// <summary>
/// Клієнти на ЧИСТОМУ ADO.NET (Microsoft.Data.SqlClient, без Dapper).
/// Тут видно все, що Dapper робить «за лаштунками»: SqlCommand, типізовані параметри,
/// ручне читання SqlDataReader у модель, прив'язка команди до транзакції UoW.
/// Свідомо НЕ успадковує Generic Repository (див. ICustomerRepository).
/// </summary>
public sealed class CustomerRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : RepositoryBase(connection, currentTransaction), ICustomerRepository
{
    private const string SelectColumns =
        "Id, Email, FullName, Phone, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    public async Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = $"SELECT {SelectColumns} FROM dbo.Customers WHERE Id = @Id AND IsDeleted = 0";

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;   // лише параметр, без конкатенації

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        // працює через UNIQUE-індекс UQ_Customers_Email
        const string sql = $"SELECT {SelectColumns} FROM dbo.Customers WHERE Email = @Email AND IsDeleted = 0";

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = email;

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<PagedResult<Customer>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct = default)
    {
        // COUNT(*) OVER() — загальна кількість в тому ж запиті, без другого звернення до БД
        const string sql = $"""
            SELECT {SelectColumns}, COUNT(*) OVER() AS TotalCount
            FROM dbo.Customers
            WHERE IsDeleted = 0
              AND (@Pattern IS NULL OR FullName LIKE @Pattern OR Email LIKE @Pattern)
            ORDER BY FullName, Id
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            """;

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Pattern", SqlDbType.NVarChar, 300).Value =
            string.IsNullOrWhiteSpace(query) ? DBNull.Value : "%" + EscapeLike(query.Trim()) + "%";
        command.Parameters.Add("@Skip", SqlDbType.Int).Value = (page - 1) * pageSize;
        command.Parameters.Add("@Take", SqlDbType.Int).Value = pageSize;

        var items = new List<Customer>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(Map(reader));
            total = reader.GetInt32(reader.GetOrdinal("TotalCount"));
        }

        return new PagedResult<Customer>(items, page, pageSize, total);
    }

    public async Task<bool> ExistsAsync(long id, CancellationToken ct = default)
    {
        const string sql = "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @Id AND IsDeleted = 0) THEN 1 ELSE 0 END";

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        return (int)(await command.ExecuteScalarAsync(ct))! == 1;
    }

    public async Task<long> AddAsync(Customer customer, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.Customers (Email, FullName, Phone, CreatedBy)
            OUTPUT INSERTED.Id
            VALUES (@Email, @FullName, @Phone, @CreatedBy)
            """;

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = customer.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 150).Value = customer.FullName;
        command.Parameters.Add("@Phone", SqlDbType.NVarChar, 20).Value = (object?)customer.Phone ?? DBNull.Value;
        command.Parameters.Add("@CreatedBy", SqlDbType.NVarChar, 100).Value = (object?)customer.CreatedBy ?? DBNull.Value;

        // дубль email → SQL 2627 → BusinessConflictException (409)
        return await TranslateErrorsAsync(async () => (long)(await command.ExecuteScalarAsync(ct))!);
    }

    public async Task UpdateAsync(Customer customer, byte[] expectedRowVersion, CancellationToken ct = default)
    {
        // Оптимістична конкурентність: рядок оновиться, лише якщо його RowVersion не змінився
        // з моменту, коли клієнт його прочитав. Інакше 0 рядків → конфлікт.
        const string sql = """
            UPDATE dbo.Customers
            SET FullName = @FullName, Phone = @Phone, Email = @Email,
                UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
            WHERE Id = @Id AND IsDeleted = 0 AND RowVersion = @RowVersion
            """;

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = customer.Id;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = customer.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 150).Value = customer.FullName;
        command.Parameters.Add("@Phone", SqlDbType.NVarChar, 20).Value = (object?)customer.Phone ?? DBNull.Value;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 100).Value = (object?)customer.UpdatedBy ?? DBNull.Value;
        command.Parameters.Add("@RowVersion", SqlDbType.Binary, 8).Value = expectedRowVersion;

        var affected = await TranslateErrorsAsync(() => command.ExecuteNonQueryAsync(ct));
        if (affected == 0)
        {
            if (!await ExistsAsync(customer.Id, ct))
                throw new NotFoundException($"Customer {customer.Id} not found");
            throw new ConcurrencyConflictException(
                $"Customer {customer.Id} was changed by someone else. Reload and try again");
        }
    }

    public async Task<bool> SoftDeleteAsync(long id, string? changedBy, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Customers
            SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
            WHERE Id = @Id AND IsDeleted = 0
            """;

        await EnsureOpenAsync(ct);
        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 100).Value = (object?)changedBy ?? DBNull.Value;
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    // ---------- допоміжні ----------

    /// <summary>Команда завжди прив'язана до з'єднання і ПОТОЧНОЇ транзакції Unit of Work.</summary>
    private SqlCommand CreateCommand(string sql) =>
        new(sql, Connection, Transaction) { CommandTimeout = CommandTimeoutSeconds };

    /// <summary>Ручне читання рядка SqlDataReader у модель (те, що Dapper робить автоматично).</summary>
    private static Customer Map(SqlDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        Email = r.GetString(r.GetOrdinal("Email")),
        FullName = r.GetString(r.GetOrdinal("FullName")),
        Phone = r.IsDBNull(r.GetOrdinal("Phone")) ? null : r.GetString(r.GetOrdinal("Phone")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
        CreatedBy = r.IsDBNull(r.GetOrdinal("CreatedBy")) ? null : r.GetString(r.GetOrdinal("CreatedBy")),
        UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt")),
        UpdatedBy = r.IsDBNull(r.GetOrdinal("UpdatedBy")) ? null : r.GetString(r.GetOrdinal("UpdatedBy")),
        IsDeleted = r.GetBoolean(r.GetOrdinal("IsDeleted")),
        RowVersion = (byte[])r["RowVersion"]
    };

    /// <summary>Екранування спецсимволів LIKE (%, _, [) — щоб «50%» шукало саме «50%».</summary>
    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
