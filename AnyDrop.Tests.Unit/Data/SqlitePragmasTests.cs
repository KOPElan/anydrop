using AnyDrop.Data;
using AnyDrop.Models;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Data;

/// <summary>
/// S0-04（DAT-03/04）：SQLite 并发写防护的机制测试。
///
/// 背景（2026-11-14 实测）：Microsoft.Data.Sqlite 的连接串**不支持**
/// <c>Journal Mode</c> / <c>Busy Timeout</c> 关键字，直接抛
/// <c>ArgumentException</c>——因此计划里「连接串配 WAL + busy_timeout」
/// 的写法不可行，改用启动 PRAGMA（WAL，按文件持久）+ 连接拦截器
/// （busy_timeout，按连接）+ 有限重试三层实现。
/// </summary>
public class SqlitePragmasTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(AppContext.BaseDirectory, "test-pragmas", $"{Guid.NewGuid():N}.db");

    private AnyDropDbContext CreateContext(Action<DbContextOptionsBuilder<AnyDropDbContext>>? configure = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var builder = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={_dbPath}");
        configure?.Invoke(builder);
        return new AnyDropDbContext(builder.Options);
    }

    [Fact]
    public void ConnectionString_JournalModeKeyword_Throws()
    {
        // 钉住约束：以后不要有人把 WAL「简化」回连接串——那样会在启动时炸掉。
        var act = () => new SqliteConnection($"Data Source={_dbPath};Journal Mode=WAL;Busy Timeout=5000");
        act.Should().Throw<ArgumentException>().WithMessage("*journal mode*");
    }

    [Fact]
    public async Task BusyTimeoutInterceptor_SetsBusyTimeoutOnOpen()
    {
        await using var db = CreateContext(b => b.AddInterceptors(new SqliteBusyTimeoutInterceptor()));
        await db.Database.OpenConnectionAsync();

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA busy_timeout;";
        Convert.ToInt32(await command.ExecuteScalarAsync())
            .Should().Be(SqliteBusyTimeoutInterceptor.BusyTimeoutMilliseconds);
    }

    [Fact]
    public async Task EnsureWalMode_SetsWalAndPersists()
    {
        await using var db = CreateContext();
        await DatabaseMigrationExtensions.EnsureWalModeAsync(db, CancellationToken.None);

        await using var check = db.Database.GetDbConnection().CreateCommand();
        check.CommandText = "PRAGMA journal_mode;";
        (await check.ExecuteScalarAsync())?.ToString().Should().Be("wal");

        // 第二个连接完全不带任何设置：模式应已持久化到库文件。
        using var second = new SqliteConnection($"Data Source={_dbPath}");
        await second.OpenAsync();
        await using var secondCheck = second.CreateCommand();
        secondCheck.CommandText = "PRAGMA journal_mode;";
        (await secondCheck.ExecuteScalarAsync())?.ToString().Should().Be("wal");
    }

    [Fact]
    public async Task SaveChangesWithRetry_RetriesThroughTransientLock()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        // 连接 A 以 BEGIN IMMEDIATE 持有写锁，250ms 后释放。
        using var locker = new SqliteConnection($"Data Source={_dbPath}");
        await locker.OpenAsync();
        await using (var begin = locker.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync();
        }

        var release = Task.Run(async () =>
        {
            await Task.Delay(250);
            await using var commit = locker.CreateCommand();
            commit.CommandText = "COMMIT;";
            await commit.ExecuteNonQueryAsync();
        });

        // 写操作在锁释放前会撞 SQLITE_BUSY（本上下文不带拦截器，
        // busy_timeout 为 0，锁不会被 SQLite 自己等过去），
        // 应由重试兜住：尝试发生在 t≈0、100、300ms，锁在 250ms 释放，
        // 第 3 次尝试成功。
        db.Topics.Add(new Topic { Id = Guid.NewGuid(), Name = "retry-probe", CreatedAt = DateTimeOffset.UtcNow });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await db.SaveChangesWithRetryAsync();
        sw.Stop();

        await release;
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(100),
            "至少经历了一次重试退避");
        (await db.Topics.CountAsync(t => t.Name == "retry-probe")).Should().Be(1);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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
