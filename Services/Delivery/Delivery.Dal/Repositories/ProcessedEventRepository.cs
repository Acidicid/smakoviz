using Dapper;
using Delivery.Dal.Abstractions;
using Delivery.Dal.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Delivery.Dal.Repositories;

/// <summary>
/// Таблиця оброблених інтеграційних подій (Лаб. 08) — ідемпотентність споживача.
/// EventId — PRIMARY KEY; рядок вставляється в ТІЙ САМІЙ транзакції Unit of Work, що й бізнес-зміни
/// (те саме з'єднання і () => _transaction), тож «оброблено» і «змінено» фіксуються атомарно.
/// </summary>
public sealed class ProcessedEventRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : RepositoryBase(connection, currentTransaction), IProcessedEventRepository
{
    public const string CreateTableSql = """
        IF OBJECT_ID(N'dbo.ProcessedEvents', N'U') IS NULL
        CREATE TABLE dbo.ProcessedEvents (
            EventId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProcessedEvents PRIMARY KEY,
            EventType   NVARCHAR(200)    NOT NULL,
            Consumer    NVARCHAR(100)    NOT NULL,
            ProcessedAt DATETIME2(3)     NOT NULL CONSTRAINT DF_ProcessedEvents_ProcessedAt DEFAULT SYSUTCDATETIME()
        );
        """;

    /// <summary>Створити таблицю, якщо її ще немає (ідемпотентно, при старті сервісу).</summary>
    public static async Task EnsureCreatedAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(CreateTableSql, cancellationToken: ct));
    }

    public async Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.ProcessedEvents WHERE EventId = @eventId",
            new { eventId }, Transaction, CommandTimeoutSeconds, cancellationToken: ct)) > 0;
    }

    public async Task<bool> TryMarkProcessedAsync(Guid eventId, string eventType, string consumer, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        try
        {
            await Connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.ProcessedEvents (EventId, EventType, Consumer) VALUES (@eventId, @eventType, @consumer)",
                new { eventId, eventType, consumer }, Transaction, CommandTimeoutSeconds, cancellationToken: ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)   // PRIMARY KEY: подію вже оброблено
        {
            return false;
        }
    }
}
