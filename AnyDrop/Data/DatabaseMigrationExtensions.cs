using System.Globalization;
using System.Reflection;
using AnyDrop.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Data;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// 基线迁移（S1-04）：仓库里唯一的迁移，包含当前模型的完整建表。
    /// 由 <see cref="Baseline"/> 类上的 <c>[Migration]</c> 特性取值，
    /// 避免时间戳硬编码与迁移类改名后脱节。
    /// </summary>
    internal static string BaselineMigrationId =>
        typeof(Baseline).GetCustomAttribute<MigrationAttribute>()?.Id
        ?? throw new InvalidOperationException("基线迁移 Baseline 缺少 [Migration] 特性。");

    /// <summary>
    /// 基线之前的最后一个旧迁移。升级门只认「恰好停在这一版」的旧库——
    /// 停在更早版本的库不能直接标记基线已应用（会缺表/缺列），
    /// 必须先升到最后一版旧发布再升级（见 README「数据库迁移」）。
    /// </summary>
    internal const string LastPreBaselineMigrationId = "20261114181120_AddPendingDeletionAndDeleteAfter";

    /// <summary>
    /// 应用 EF Core 迁移，并执行必要的历史兼容步骤。
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        // S1-03：上下文由工厂创建、随本次启动迁移用完即弃，不再取请求作用域的实例。
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<AnyDropDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);

        // S0-04：先开 WAL 再跑迁移（迁移本身也是写操作，同样受益）。
        // Microsoft.Data.Sqlite 的连接串不支持 Journal Mode 关键字，
        // 因此 WAL 只能这样按库文件设置；模式会持久化到库文件本身。
        await EnsureWalModeAsync(db, ct);
        // S1-04：旧库升级门。停在最后一版旧迁移的库不会再跑基线的建表，
        // 而是校验后把基线标记为已应用；新库则正常由 MigrateAsync 应用基线。
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var migrationLogger = loggerFactory?.CreateLogger("AnyDrop.Data.DatabaseMigrationExtensions");
        await MarkBaselineAppliedIfOldHeadAsync(db, migrationLogger, ct);
        await db.Database.MigrateAsync(ct);
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
    /// 旧库升级门（S1-04，DAT-10）：把「停在最后一版旧迁移」的数据库
    /// 平滑接到唯一的基线迁移上。
    ///
    /// 背景：基线迁移之前有 13 个旧迁移；旧库的 <c>__EFMigrationsHistory</c>
    /// 里是旧 ID，基线若直接跑 <c>Up()</c> 会因表已存在而失败。
    /// 因此这里在 <c>MigrateAsync</c> 之前做一次显式判定：
    /// <list type="bullet">
    ///   <item>没有迁移历史表 → 全新库，返回 false，由 MigrateAsync 正常应用基线；</item>
    ///   <item>历史里已有基线 ID → 已是基线库，返回 false；</item>
    ///   <item>历史里有最后一版旧迁移 ID → 旧库：校验表结构完整
    ///   （5 张表 + 设置种子行）并补齐启动补丁列后，把基线 ID 写入历史，
    ///   返回 true（MigrateAsync 随后无事可做）；</item>
    ///   <item>其他情况（停在中间版本、或来历不明的历史）→ 直接抛错拒绝启动，
    ///   不猜测、不自动修——先备份，再按 README 指引升到最后一版旧发布。</item>
    /// </list>
    /// 旧的启动补丁 <c>EnsureScheduledThumbnailGenerationColumnAsync</c> 已随
    /// 旧迁移一并删除：它的列如今由基线建表（新库）或本门的补齐步骤（旧库）负责，
    /// 双轨就此终结。返回 true 表示已写入基线记录。
    /// </summary>
    internal static async Task<bool> MarkBaselineAppliedIfOldHeadAsync(
        AnyDropDbContext dbContext,
        ILogger? logger,
        CancellationToken ct)
    {
        if (!string.Equals(
                dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
        {
            return false;
        }

        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        if (!await TableExistsAsync(connection, "__EFMigrationsHistory", ct))
        {
            return false;
        }

        var appliedIds = await ReadMigrationHistoryAsync(connection, ct);
        var baselineId = BaselineMigrationId;
        if (appliedIds.Contains(baselineId, StringComparer.Ordinal))
        {
            return false;
        }

        if (!appliedIds.Contains(LastPreBaselineMigrationId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "数据库迁移历史无法识别：既没有基线迁移记录，也没有最后一版旧迁移"
                + $"（{LastPreBaselineMigrationId}）的记录。已应用的记录为："
                + (appliedIds.Count == 0 ? "（无）" : string.Join(", ", appliedIds))
                + "。服务端拒绝启动。请先备份 data/anydrop.db，再按 README「数据库迁移」一节处理"
                + "（停在中间版本的旧库须先升到最后一版旧发布，或从备份恢复）。");
        }

        // 旧库：先校验表结构完整，再标记。缺表/缺种子行说明库已损坏或迁移不完整，
        // 标记基线会掩盖问题，因此直接拒绝启动。
        foreach (var table in new[] { "Users", "Topics", "ShareItems", "SystemSettings", "PendingFileDeletions" })
        {
            if (!await TableExistsAsync(connection, table, ct))
            {
                throw new InvalidOperationException(
                    $"数据库迁移历史停在最后一版旧迁移，但表 {table} 不存在，库结构不完整。"
                    + "服务端拒绝启动。请先备份 data/anydrop.db，再按 README「数据库迁移」一节从备份恢复或修复。");
            }
        }

        await using (var seedCommand = connection.CreateCommand())
        {
            seedCommand.CommandText = "SELECT COUNT(*) FROM SystemSettings WHERE Id = '11111111-1111-1111-1111-111111111111';";
            var seedCount = Convert.ToInt32(await seedCommand.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (seedCount != 1)
            {
                throw new InvalidOperationException(
                    "数据库迁移历史停在最后一版旧迁移，但 SystemSettings 种子行缺失或重复，库结构不完整。"
                    + "服务端拒绝启动。请先备份 data/anydrop.db，再按 README「数据库迁移」一节从备份恢复或修复。");
            }
        }

        // 补齐历史上由启动补丁创建的那一列：旧库若从未跑过带补丁的版本会缺它，
        // 而基线一旦被标记为已应用，EF 就不会再建它。
        if (!await ColumnExistsAsync(connection, "SystemSettings", "ScheduledThumbnailGenerationEnabled", ct))
        {
            await using var addColumn = connection.CreateCommand();
            addColumn.CommandText =
                "ALTER TABLE SystemSettings ADD COLUMN ScheduledThumbnailGenerationEnabled INTEGER NOT NULL DEFAULT 0;";
            await addColumn.ExecuteNonQueryAsync(ct);
        }

        var productVersion = typeof(DbContext).Assembly.GetName().Version;
        var versionText = productVersion is null
            ? "10.0.0"
            : $"{productVersion.Major}.{productVersion.Minor}.{productVersion.Build}";
        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText =
            "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ($id, $version);";
        insertCommand.Parameters.AddWithValue("$id", baselineId);
        insertCommand.Parameters.AddWithValue("$version", versionText);
        await insertCommand.ExecuteNonQueryAsync(ct);

        // 升级事件记一条 Warning：运维者在启动日志里必须能看到「旧库已接基线」，
        // 而不是静默地以为这是个新库。
        logger?.LogWarning(
            "检测到旧版迁移历史（已到 {LastOldMigration}），已将基线迁移 {BaselineMigration} 标记为已应用，表结构未重建，数据保持不变。",
            LastPreBaselineMigrationId,
            baselineId);

        return true;
    }

    private static async Task<List<string>> ReadMigrationHistoryAsync(SqliteConnection connection, CancellationToken ct)
    {
        var ids = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader["MigrationId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
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
