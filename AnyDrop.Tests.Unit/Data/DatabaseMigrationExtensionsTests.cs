using AnyDrop.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Data;

/// <summary>
/// 针对「模型期望 <c>SystemSettings.ScheduledThumbnailGenerationEnabled</c> 列，
/// 但没有任何迁移会创建它」的回归测试。
///
/// 背景：该列对应的迁移文件缺少 <c>.Designer.cs</c>，因而没有 <c>[DbContext]</c> 特性，
/// EF Core 从未发现它，也就从未被应用。此前是靠一个启动补丁建列的；补丁一旦被移除，
/// 任何读取 SystemSettings 全字段的接口（例如 <c>/api/v1/settings/security</c>）都会以
/// <c>no such column</c> 直接 500。
///
/// 这组测试用**真实的 SQLite 数据库**完整走一遍「应用迁移 + 兼容步骤」，
/// 正是此前缺失的一环——其余测试全部基于 InMemory provider，根本不执行迁移。
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

    private static async Task<bool> ColumnExistsAsync(AnyDropDbContext db, string columnName)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(SystemSettings);";
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

    [Fact]
    public async Task MigrateThenEnsure_MakesFullEntityQueryable()
    {
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        (await ColumnExistsAsync(db, ColumnName)).Should().BeFalse(
            "迁移集合中没有任何一个会创建该列——这正是需要兼容步骤的原因");

        await DatabaseMigrationExtensions.EnsureScheduledThumbnailGenerationColumnAsync(
            db, CancellationToken.None);

        (await ColumnExistsAsync(db, ColumnName)).Should().BeTrue();

        // 关键回归断言：读取全部字段不再抛 "no such column"
        var settings = await db.SystemSettings.AsNoTracking().ToListAsync();
        settings.Should().NotBeEmpty("迁移中的 HasData 会写入默认设置行");
        settings[0].ScheduledThumbnailGenerationEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureColumn_IsIdempotent()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        await DatabaseMigrationExtensions.EnsureScheduledThumbnailGenerationColumnAsync(
            db, CancellationToken.None);

        var secondRun = async () => await DatabaseMigrationExtensions.EnsureScheduledThumbnailGenerationColumnAsync(
            db, CancellationToken.None);

        await secondRun.Should().NotThrowAsync("列已存在时应直接返回，不重复执行 ALTER");
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
