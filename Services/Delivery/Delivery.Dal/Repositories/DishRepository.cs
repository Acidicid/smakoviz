using Dapper;
using Delivery.Dal.Abstractions;
using Delivery.Dal.Infrastructure;
using Delivery.Domain.Models;
using Microsoft.Data.SqlClient;

namespace Delivery.Dal.Repositories;

/// <summary>
/// Локальна копія страв (ADO.NET + Dapper). Спільний CRUD — у BaseDapperRepository,
/// тут лише специфічні запити. Id не IDENTITY — він приходить з БД Меню, тому
/// входить у список колонок для INSERT.
/// </summary>
public sealed class DishRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : BaseDapperRepository<Dish>(connection, currentTransaction,
        tableName: "dbo.Dishes",
        insertColumns: ["Id", "RestaurantId", "Name", "Price", "IsAvailable"],
        softDelete: false),
      IDishRepository
{
    public async Task<IReadOnlyList<Dish>> GetByRestaurantAsync(long restaurantId, bool onlyAvailable, CancellationToken ct = default)
    {
        // для onlyAvailable = 1 працює фільтрований індекс IX_Dishes_Restaurant_Available
        const string sql = """
            SELECT Id, RestaurantId, Name, Price, IsAvailable, SyncedAt
            FROM dbo.Dishes
            WHERE RestaurantId = @RestaurantId AND (@OnlyAvailable = 0 OR IsAvailable = 1)
            ORDER BY Name
            """;

        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<Dish>(
            Command(sql, new { RestaurantId = restaurantId, OnlyAvailable = onlyAvailable }, ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Dish>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        // Dapper розгортає список у параметри: IN (@Ids1, @Ids2, ...) — без конкатенації значень
        const string sql = "SELECT Id, RestaurantId, Name, Price, IsAvailable, SyncedAt FROM dbo.Dishes WHERE Id IN @Ids";

        await EnsureOpenAsync(ct);
        var rows = await Connection.QueryAsync<Dish>(Command(sql, new { Ids = ids }, ct));
        return rows.AsList();
    }

    public async Task UpsertAsync(Dish dish, CancellationToken ct = default)
    {
        const string sql = """
            MERGE dbo.Dishes AS t
            USING (SELECT @Id AS Id) AS s ON t.Id = s.Id
            WHEN MATCHED THEN
                UPDATE SET RestaurantId = @RestaurantId, Name = @Name, Price = @Price,
                           IsAvailable = @IsAvailable, SyncedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (Id, RestaurantId, Name, Price, IsAvailable)
                VALUES (@Id, @RestaurantId, @Name, @Price, @IsAvailable);
            """;

        await EnsureOpenAsync(ct);
        await TranslateErrorsAsync(() => Connection.ExecuteAsync(Command(sql, dish, ct)));
    }
}
