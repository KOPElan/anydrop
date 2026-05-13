using AnyDrop.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public static class DatabaseMigrationExtensions
{
    public static async Task MigrateAndSeedAsync(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnyDropDbContext>();

        await db.Database.MigrateAsync(ct);
        await EnsureScheduledThumbnailGenerationColumnAsync(db, ct);

        if (!await db.SystemSettings.AnyAsync(ct))
        {
            db.SystemSettings.Add(new SystemSettings
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                AutoFetchLinkPreview = true,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task EnsureScheduledThumbnailGenerationColumnAsync(AnyDropDbContext dbContext, CancellationToken ct)
    {
        if (!string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            return;
        }

        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        if (!await TableExistsAsync(connection, "SystemSettings", ct))
        {
            return;
        }

        if (await ColumnExistsAsync(connection, "SystemSettings", "ScheduledThumbnailGenerationEnabled", ct))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE SystemSettings ADD COLUMN ScheduledThumbnailGenerationEnabled INTEGER NOT NULL DEFAULT 0;";
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table LIMIT 1;";
        command.Parameters.AddWithValue("$table", tableName);
        var result = await command.ExecuteScalarAsync(ct);
        return result is not null;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
