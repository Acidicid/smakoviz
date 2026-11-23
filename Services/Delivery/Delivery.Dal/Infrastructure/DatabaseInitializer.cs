using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Delivery.Dal.Infrastructure;

/// <summary>
/// Розгортає базу Проєкту №1 скриптами з Лаб. 01 (db/p1/schema.sql → procedures.sql → seed.sql),
/// які вбудовані в цю збірку. Скрипти ідемпотентні, тож запуск на вже готовій базі нічого не ламає.
/// Потрібно, коли Aspire піднімає НОВИЙ порожній контейнер SQL Server.
/// Вмикається налаштуванням Database:Initialize = true.
/// </summary>
public static partial class DatabaseInitializer
{
    public static async Task InitializeAsync(string connectionString, ILogger logger, CancellationToken ct = default)
    {
        // скрипти самі створюють БД (CREATE DATABASE) і роблять USE, тому підключаємось до master
        var masterConnection = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }
            .ConnectionString;

        var assembly = typeof(DatabaseInitializer).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("DbScripts.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        await using var connection = await OpenWithRetryAsync(masterConnection, logger, ct);

        foreach (var script in scripts)
        {
            logger.LogInformation("Applying database script {Script}", script);
            var text = await ReadResourceAsync(assembly, script);

            // GO — не SQL, а роздільник пакетів для SSMS/sqlcmd; виконуємо пакети по одному
            foreach (var batch in GoSeparator().Split(text).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
                await command.ExecuteNonQueryAsync(ct);
            }
        }

        logger.LogInformation("Database is ready ({Count} scripts applied)", scripts.Count);
    }

    private static async Task<SqlConnection> OpenWithRetryAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        // контейнер SQL Server стартує 10–60 с — чекаємо, а не падаємо одразу
        for (var attempt = 1; ; attempt++)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(ct);
                return connection;
            }
            catch (SqlException) when (attempt < 30)
            {
                await connection.DisposeAsync();
                logger.LogWarning("SQL Server is not ready yet (attempt {Attempt}/30), retrying in 3 s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    private static async Task<string> ReadResourceAsync(Assembly assembly, string name)
    {
        await using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded script {name} not found");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
