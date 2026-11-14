using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S0-08（DAT-03/04）回归组：并发写压力。
///
/// 模拟「并发上传 + 过期清理」同时写库：8 个并行任务各发 25 条文本消息
/// （每个任务用独立 DbContext，共享同一个 SQLite 文件），期间另有任务
/// 反复读列表与跑清理。断言：无任何 <c>SqliteException</c>
/// （database is locked / busy），最终行数正确。
/// 这是 S0-04 三层防护（WAL + busy_timeout + 有限重试）的直接验收；
/// 计划里 30 分钟浸泡运行不在 CI 做，此处用高并发短时压测代替，
/// 30 分钟浸泡仍需在部署后手工跑（见最终报告）。
/// </summary>
public class ConcurrentWriteStressTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(AppContext.BaseDirectory, "test-stress", $"{Guid.NewGuid():N}.db");

    private AnyDropDbContext CreateContext()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(new SqliteBusyTimeoutInterceptor())
            .Options;
        return new AnyDropDbContext(options);
    }

    [Fact]
    public async Task ParallelWriters_NoDatabaseLockedErrors()
    {
        // 建库 + 开 WAL（与启动路径一致）
        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();
            await DatabaseMigrationExtensions.EnsureWalModeAsync(setup, CancellationToken.None);
        }

        const int writers = 8;
        const int messagesPerWriter = 25;

        var writerTasks = Enumerable.Range(0, writers).Select(async writer =>
        {
            await using var db = CreateContext();
            var service = ShareServiceFactory.Create(db);
            for (var i = 0; i < messagesPerWriter; i++)
            {
                await service.SendTextAsync($"writer {writer} message {i}");
            }
        });

        var readerTask = Task.Run(async () =>
        {
            await using var db = CreateContext();
            var service = ShareServiceFactory.Create(db);
            for (var i = 0; i < 50; i++)
            {
                _ = await service.GetRecentAsync();
                await Task.Delay(10);
            }
        });

        var cleanerTask = Task.Run(async () =>
        {
            await using var db = CreateContext();
            var service = ShareServiceFactory.Create(db);
            for (var i = 0; i < 20; i++)
            {
                _ = await service.CleanupOldMessagesAsync(1);
                await Task.Delay(25);
            }
        });

        // 任一任务抛 SqliteException 即失败
        await Task.WhenAll(writerTasks.Append(readerTask).Append(cleanerTask));

        await using var verify = CreateContext();
        (await verify.ShareItems.CountAsync()).Should().Be(writers * messagesPerWriter);
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
