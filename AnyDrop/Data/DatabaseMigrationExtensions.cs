using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// 应用 EF Core 迁移，并执行必要的历史兼容步骤。
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnyDropDbContext>();

        await db.Database.MigrateAsync(ct);
        await EnsureScheduledThumbnailGenerationColumnAsync(db, ct);
    }

    /// <summary>
    /// 确保 <c>SystemSettings.ScheduledThumbnailGenerationEnabled</c> 列存在（幂等）。
    ///
    /// 为什么需要这一步而不是靠迁移：
    ///
    /// 对应的迁移文件 <c>20260513141000_AddScheduledThumbnailGenerationFlag.cs</c> 一直缺少
    /// <c>.Designer.cs</c>（迁移集合里唯一缺的一个），而 <c>[DbContext(typeof(AnyDropDbContext))]</c>
    /// 特性正是写在那里面。EF Core 只发现带该特性的迁移，因此这个迁移**从未被应用到任何数据库**。
    /// 本列一直是靠这段启动补丁创建的——它并不是「绕过迁移历史」，而是在弥补一个坏掉的迁移。
    ///
    /// 也不能简单地把迁移补好让它可被发现：现存数据库都已有该列（由本补丁创建）却没有迁移记录，
    /// 一旦该迁移被应用就会因 duplicate column 而启动失败。
    ///
    /// 因此这里保留了幂等的列检查。等到不再需要兼容这些历史数据库时，可以删除本方法，
    /// 并把该列并入一个正常的迁移（届时那个坏迁移文件也应一并清理）。
    /// </summary>
    internal static async Task EnsureScheduledThumbnailGenerationColumnAsync(
        AnyDropDbContext dbContext,
        CancellationToken ct)
    {
        if (!string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
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
        command.CommandText =
            "ALTER TABLE SystemSettings ADD COLUMN ScheduledThumbnailGenerationEnabled INTEGER NOT NULL DEFAULT 0;";
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

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken ct)
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
