using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public static class DatabaseMigrationExtensions
{
    private const string InitialMigrationId = "20260418183210_InitialCreate";
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";
    private const string ProductVersion = "10.0.0";

    /// <summary>
    /// Applies EF Core migrations and repairs legacy SQLite databases that already contain the current table schema.
    /// </summary>
    public static async Task MigrateWithCompatibilityAsync(
        this AnyDropDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                                         ex.Message.Contains("table \"ShareItems\" already exists", StringComparison.OrdinalIgnoreCase))
        {
            await RepairLegacyDatabaseAsync(dbContext, cancellationToken);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
    }

    private static async Task RepairLegacyDatabaseAsync(
        AnyDropDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(dbContext, "ShareItems", cancellationToken))
        {
            throw new InvalidOperationException("The existing SQLite database does not contain the expected ShareItems table.");
        }

        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            using var createHistoryTableCommand = connection.CreateCommand();
            createHistoryTableCommand.CommandText = $"""
                CREATE TABLE IF NOT EXISTS "{MigrationsHistoryTable}" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK_{MigrationsHistoryTable}" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                """;

            await createHistoryTableCommand.ExecuteNonQueryAsync(cancellationToken);

            using var insertMigrationCommand = connection.CreateCommand();
            insertMigrationCommand.CommandText = $"""
                INSERT OR IGNORE INTO "{MigrationsHistoryTable}" ("MigrationId", "ProductVersion")
                VALUES ($migrationId, $productVersion);
                """;

            var migrationIdParameter = insertMigrationCommand.CreateParameter();
            migrationIdParameter.ParameterName = "$migrationId";
            migrationIdParameter.Value = InitialMigrationId;
            insertMigrationCommand.Parameters.Add(migrationIdParameter);

            var productVersionParameter = insertMigrationCommand.CreateParameter();
            productVersionParameter.ParameterName = "$productVersion";
            productVersionParameter.Value = ProductVersion;
            insertMigrationCommand.Parameters.Add(productVersionParameter);

            await insertMigrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> TableExistsAsync(
        AnyDropDbContext dbContext,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table' AND name = $tableName
                LIMIT 1;
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "$tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is not null;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}
