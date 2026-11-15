using System.Globalization;
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

        // S0-04：先开 WAL 再跑迁移（迁移本身也是写操作，同样受益）。
        // Microsoft.Data.Sqlite 的连接串不支持 Journal Mode 关键字，
        // 因此 WAL 只能这样按库文件设置；模式会持久化到库文件本身。
        await EnsureWalModeAsync(db, ct);
        await db.Database.MigrateAsync(ct);
        await EnsureScheduledThumbnailGenerationColumnAsync(db, ct);
        await EnsureUserSingletonIndexAsync(db, ct);
    }

    /// <summary>
    /// 对库文件启用 WAL 模式（S0-04，DAT-03）。
    /// 成功时 PRAGMA 返回 <c>wal</c>；返回其他值（例如文件系统不支持）
    /// 时直接启动失败并给出明确信息——并发写防护依赖 WAL，不能静默降级。
    /// </summary>
    internal static async Task EnsureWalModeAsync(AnyDropDbContext dbContext, CancellationToken ct)
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

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        var mode = (await command.ExecuteScalarAsync(ct))?.ToString();
        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"无法为 SQLite 数据库启用 WAL 模式（PRAGMA journal_mode 返回 '{mode}'）。" +
                "WAL 是并发写入防护的前提，请检查数据库文件所在文件系统是否支持 WAL。");
        }
    }

    /// <summary>
    /// 启动即校验 Users 表存在 SingletonKey 唯一索引（单用户保护）。
    ///
    /// S0-03（SEC-02/04/05）：该索引是「全库只能有一个用户」的唯一防线。
    /// 若历史数据库迁移不完整导致索引缺失，继续运行会允许创建第二个用户，
    /// 因此直接启动失败并给出明确信息，而不是静默放行。
    /// </summary>
    internal static async Task EnsureUserSingletonIndexAsync(
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

        const string errorMessage =
            "Users 表缺少 SingletonKey 唯一索引（单用户保护失效）。请先备份 data/anydrop.db，" +
            "再检查数据库迁移是否完整应用（dotnet ef database update），或按 README 的修复指引重建索引；" +
            "缺失该索引时服务端拒绝启动。";

        if (!await TableExistsAsync(connection, "Users", ct))
        {
            throw new InvalidOperationException(errorMessage);
        }

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = "PRAGMA index_list(Users);";
        await using var indexReader = await indexCommand.ExecuteReaderAsync(ct);
        var uniqueIndexNames = new List<string>();
        while (await indexReader.ReadAsync(ct))
        {
            var isUnique = Convert.ToInt32(indexReader["unique"], CultureInfo.InvariantCulture) == 1;
            var name = indexReader["name"]?.ToString();
            if (isUnique && !string.IsNullOrWhiteSpace(name))
            {
                uniqueIndexNames.Add(name);
            }
        }

        await indexReader.CloseAsync();

        foreach (var indexName in uniqueIndexNames)
        {
            await using var infoCommand = connection.CreateCommand();
            infoCommand.CommandText = $"PRAGMA index_info({QuoteIdentifier(indexName)});";
            await using var infoReader = await infoCommand.ExecuteReaderAsync(ct);
            var columns = new List<string>();
            while (await infoReader.ReadAsync(ct))
            {
                columns.Add(infoReader["name"]?.ToString() ?? string.Empty);
            }

            if (columns.Count == 1 && string.Equals(columns[0], "SingletonKey", StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(errorMessage);
    }

    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

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
