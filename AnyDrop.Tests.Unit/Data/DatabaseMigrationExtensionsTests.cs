using AnyDrop.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Data;

/// <summary>
/// S1-04（DAT-10）回归组：基线迁移 + 旧库升级门。
///
/// 背景：基线之前有 13 个旧迁移，其中 <c>20260513141000_AddScheduledThumbnailGenerationFlag</c>
/// 迁移文件缺失（.Designer.cs 不存在，文件本身也不在仓库里），EF Core 从未应用过它；
/// <c>SystemSettings.ScheduledThumbnailGenerationEnabled</c> 列一直靠启动补丁创建。
/// S1-04 用一个基线迁移替代全部旧迁移并删除补丁，旧库由升级门接过去。
///
/// 这组测试用**真实的 SQLite 数据库**完整走一遍迁移与升级门——
/// 其余测试全部基于 InMemory provider，根本不执行迁移。
/// </summary>
public class DatabaseMigrationExtensionsTests : IDisposable
{
    private const string ColumnName = "ScheduledThumbnailGenerationEnabled";

    private readonly string _dbPath =
        Path.Combine(AppContext.BaseDirectory, "test-migrations", $"{Guid.NewGuid():N}.db");

    private AnyDropDbContext CreateContext()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        return new AnyDropDbContext(options);
    }

    private static async Task<bool> ColumnExistsAsync(AnyDropDbContext db, string columnName, string tableName = "SystemSettings")
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 基线迁移在全新库上建出完整结构：5 张表、种子行、补丁列直接由迁移创建
    /// （不再靠启动补丁），且已删除的 TimeZoneId 列不在新库里。
    /// </summary>
    [Fact]
    public async Task BaselineMigration_CreatesFullSchema()
    {
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        (await ColumnExistsAsync(db, ColumnName)).Should().BeTrue(
            "基线迁移应直接创建该列，启动补丁已删除");
        (await ColumnExistsAsync(db, "TimeZoneId")).Should().BeFalse(
            "S1-07 已删除死字段 SystemSettings.TimeZoneId，新库不应再有该列");

        // 关键回归断言：读取全部字段不再抛 "no such column"
        var settings = await db.SystemSettings.AsNoTracking().ToListAsync();
        settings.Should().HaveCount(1, "基线迁移的 HasData 会写入默认设置行");
        settings[0].ScheduledThumbnailGenerationEnabled.Should().BeFalse();

        foreach (var table in new[] { "Users", "Topics", "ShareItems", "SystemSettings", "PendingFileDeletions" })
        {
            (await TableExistsAsync(db, table)).Should().BeTrue($"基线迁移应创建 {table} 表");
        }
    }

    /// <summary>全新库没有迁移历史表，升级门直接放行，由 MigrateAsync 应用基线。</summary>
    [Fact]
    public async Task Gate_FreshDb_ReturnsFalse()
    {
        await using var db = CreateContext();

        var marked = await DatabaseMigrationExtensions.MarkBaselineAppliedIfOldHeadAsync(
            db, null, CancellationToken.None);

        marked.Should().BeFalse();
    }

    /// <summary>已是基线库时升级门不做任何事。</summary>
    [Fact]
    public async Task Gate_BaselineDb_ReturnsFalse()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var marked = await DatabaseMigrationExtensions.MarkBaselineAppliedIfOldHeadAsync(
            db, null, CancellationToken.None);

        marked.Should().BeFalse();
    }

    /// <summary>
    /// 模拟旧库：历史改写为最后一版旧迁移、删掉新迁移带来的结构（UploadState 列、
    /// MissingFileReports 表、RefreshTokens 表）与补丁列（模拟从未跑过补丁的旧库），
    /// 升级门应校验通过、补齐列、把基线记入历史；随后 MigrateAsync 应用新迁移。
    /// </summary>
    [Fact]
    public async Task Gate_OldHeadDb_MarksBaselineAndBackfillsColumn()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await RewriteHistoryAsync(db, DatabaseMigrationExtensions.LastPreBaselineMigrationId);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE ShareItems DROP COLUMN UploadState;");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE MissingFileReports;");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE RefreshTokens;");
        await db.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE SystemSettings DROP COLUMN {ColumnName};");
        (await ColumnExistsAsync(db, ColumnName)).Should().BeFalse("前置条件：模拟旧库缺补丁列");

        var marked = await DatabaseMigrationExtensions.MarkBaselineAppliedIfOldHeadAsync(
            db, null, CancellationToken.None);

        marked.Should().BeTrue();
        (await ColumnExistsAsync(db, ColumnName)).Should().BeTrue("升级门应补齐补丁列");
        (await ReadHistoryAsync(db)).Should().Contain(DatabaseMigrationExtensions.BaselineMigrationId);

        // 随后 MigrateAsync 应用基线之后的新迁移（UploadState 列 + 修复记录表 + RefreshTokens 表），
        // 不能因表已存在而失败
        await db.Database.MigrateAsync();
        (await ColumnExistsAsync(db, "UploadState", "ShareItems")).Should().BeTrue("新迁移应补上 UploadState 列");
        (await TableExistsAsync(db, "MissingFileReports")).Should().BeTrue("新迁移应创建修复记录表");
        (await TableExistsAsync(db, "RefreshTokens")).Should().BeTrue("新迁移应创建 RefreshTokens 表");
        (await db.SystemSettings.CountAsync()).Should().Be(1);
    }

    /// <summary>停在中间版本（非最后一版旧迁移）的库不能直接接基线，必须先升上去。</summary>
    [Fact]
    public async Task Gate_IntermediateOldDb_Throws()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await RewriteHistoryAsync(db, "20260512142059_AddThumbnailFields");

        var act = async () => await DatabaseMigrationExtensions.MarkBaselineAppliedIfOldHeadAsync(
            db, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*无法识别*");
    }

    /// <summary>来历不明的迁移历史不猜测，直接拒绝启动。</summary>
    [Fact]
    public async Task Gate_UnknownHistory_Throws()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await RewriteHistoryAsync(db, "20990101000000_SomeoneElsesMigration");

        var act = async () => await DatabaseMigrationExtensions.MarkBaselineAppliedIfOldHeadAsync(
            db, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*无法识别*");
    }

    /// <summary>历史停在最后一版但缺表——标记基线会掩盖损坏，必须拒绝启动。</summary>
    [Fact]
    public async Task Gate_OldHeadMissingTable_Throws()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await RewriteHistoryAsync(db, DatabaseMigrationExtensions.LastPreBaselineMigrationId);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE PendingFileDeletions;");

        var act = async () => await DatabaseMigrationExtensions.MarkBaselineAppliedIfOldHeadAsync(
            db, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PendingFileDeletions*不存在*");
    }

    private static async Task RewriteHistoryAsync(AnyDropDbContext db, string migrationId)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory;");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({0}, '10.0.0');",
            migrationId);
    }

    private static async Task<List<string>> ReadHistoryAsync(AnyDropDbContext db)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var ids = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader["MigrationId"]?.ToString() ?? string.Empty);
        }

        return ids;
    }

    private static async Task<bool> TableExistsAsync(AnyDropDbContext db, string tableName)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table LIMIT 1;";
        command.Parameters.AddWithValue("$table", tableName);
        return await command.ExecuteScalarAsync() is not null;
    }

    [Fact]
    public async Task MigrationsDoApplyTheDefaultSettingsRow()
    {
        // 迭代 3 移除了运行时种子逻辑，依据是「默认行由 HasData 随迁移写入」。
        // 这里把这个依据钉住，避免再次凭假设删除代码。
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        (await db.SystemSettings.CountAsync()).Should().Be(1);
    }

    // S0-03：启动时校验 Users 表 SingletonKey 唯一索引（单用户保护的唯一防线）。
    [Fact]
    public async Task EnsureUserSingletonIndex_PassesAfterMigrations()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var check = async () => await DatabaseMigrationExtensions.EnsureUserSingletonIndexAsync(
            db, CancellationToken.None);

        await check.Should().NotThrowAsync("正常迁移后的数据库一定有该索引");
    }

    [Fact]
    public async Task EnsureUserSingletonIndex_ThrowsWhenIndexMissing()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        // 模拟历史数据库迁移不完整：删掉单例索引
        var indexName = await FindSingletonIndexNameAsync(db);
        indexName.Should().NotBeNull("迁移后的数据库应存在 SingletonKey 唯一索引");
        await DropIndexAsync(db, indexName!);

        var check = async () => await DatabaseMigrationExtensions.EnsureUserSingletonIndexAsync(
            db, CancellationToken.None);

        await check.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SingletonKey*");
    }

    private static async Task<string?> FindSingletonIndexNameAsync(AnyDropDbContext db)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var listCommand = connection.CreateCommand();
        listCommand.CommandText = "PRAGMA index_list(Users);";
        await using var listReader = await listCommand.ExecuteReaderAsync();
        var names = new List<string>();
        while (await listReader.ReadAsync())
        {
            if (Convert.ToInt32(listReader["unique"], System.Globalization.CultureInfo.InvariantCulture) == 1)
            {
                names.Add(listReader["name"]?.ToString() ?? string.Empty);
            }
        }

        await listReader.CloseAsync();

        foreach (var name in names)
        {
            await using var infoCommand = connection.CreateCommand();
            infoCommand.CommandText = $"PRAGMA index_info(\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\");";
            await using var infoReader = await infoCommand.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await infoReader.ReadAsync())
            {
                columns.Add(infoReader["name"]?.ToString() ?? string.Empty);
            }

            if (columns.Count == 1 && columns[0] == "SingletonKey")
            {
                return name;
            }
        }

        return null;
    }

    private static async Task DropIndexAsync(AnyDropDbContext db, string indexName)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP INDEX \"{indexName.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        var directory = Path.GetDirectoryName(_dbPath);
        if (directory is not null
            && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }
}
