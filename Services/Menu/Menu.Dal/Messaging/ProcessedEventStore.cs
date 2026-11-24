using Menu.Dal.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Menu.Dal.Messaging;

/// <summary>
/// Таблиця оброблених подій (Лаб. 08) — ідемпотентність споживача: EventId — PRIMARY KEY,
/// тож повторна доставка тієї самої події не застосує бізнес-зміни вдруге.
/// Рядок вставляється в ТІЙ САМІЙ транзакції, що й бізнес-зміни (через Database.BeginTransaction EF Core):
/// або обидва записи збережено, або жодного.
///
/// Таблиця не входить у модель EF (не потребує нової міграції — модель з Лаб. 03 не змінюється);
/// створюється ідемпотентним SQL при старті сервісу.
/// </summary>
public static class ProcessedEventStore
{
    private const string Table = "dbo.ProcessedEvents";

    public static Task EnsureCreatedAsync(MenuDbContext db, CancellationToken ct = default)
        => db.Database.ExecuteSqlRawAsync($"""
            IF OBJECT_ID(N'{Table}', N'U') IS NULL
            CREATE TABLE {Table} (
                EventId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProcessedEvents PRIMARY KEY,
                EventType   NVARCHAR(200)    NOT NULL,
                Consumer    NVARCHAR(100)    NOT NULL,
                ProcessedAt DATETIME2(3)     NOT NULL CONSTRAINT DF_ProcessedEvents_ProcessedAt DEFAULT SYSUTCDATETIME()
            );
            """, ct);

    /// <summary>Чи подію вже оброблено (швидка перевірка до транзакції).</summary>
    public static async Task<bool> IsProcessedAsync(MenuDbContext db, Guid eventId, CancellationToken ct = default)
        => await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.ProcessedEvents WHERE EventId = {eventId}")
            .SingleAsync(ct) > 0;

    /// <summary>
    /// Позначити подію обробленою в поточній транзакції. false — її вже обробив паралельний споживач
    /// (порушення PRIMARY KEY 2627/2601) → бізнес-зміни треба відкотити.
    /// </summary>
    public static async Task<bool> TryMarkProcessedAsync(MenuDbContext db, Guid eventId, string eventType, string consumer,
        CancellationToken ct = default)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO dbo.ProcessedEvents (EventId, EventType, Consumer) VALUES ({eventId}, {eventType}, {consumer})", ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;
        }
    }
}
