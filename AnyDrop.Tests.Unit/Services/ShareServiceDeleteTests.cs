using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S0-05（DAT-01，D5）：删除顺序反转 + 延迟删除标记 + 待删除队列。
/// </summary>
public class ShareServiceDeleteTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(AppContext.BaseDirectory, "test-delete", $"{Guid.NewGuid():N}.db");

    private static TestDbContextFactory CreateInMemoryDbFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-delete-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static ShareItem SeedFileItem(AnyDropDbContext db, DateTimeOffset createdAt)
    {
        var item = new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.File,
            Content = "20261114/deadbeef.png",
            FileName = "deadbeef.png",
            MimeType = "image/png",
            ThumbnailPath = "thumbnails/deadbeef.jpg",
            CreatedAt = createdAt
        };
        db.ShareItems.Add(item);
        db.SaveChanges();
        return item;
    }

    [Fact]
    public async Task DeleteShareItemsAsync_RemovesRowBeforeDeletingFiles()
    {
        var factory = CreateInMemoryDbFactory();
        await using var db = factory.CreateDbContext();
        var item = SeedFileItem(db, DateTimeOffset.UtcNow);
        var sut = ShareServiceFactory.Create(factory);

        var deleted = await sut.DeleteShareItemsAsync([item.Id]);

        deleted.Should().Be(1);
        (await db.ShareItems.CountAsync()).Should().Be(0, "数据库行已提交删除");
    }

    [Fact]
    public async Task DeleteShareItemsAsync_FileDeleteFailure_IsQueuedNotLost()
    {
        var factory = CreateInMemoryDbFactory();
        await using var db = factory.CreateDbContext();
        var item = SeedFileItem(db, DateTimeOffset.UtcNow);

        var fileStorage = new Mock<IFileStorageService>();
        fileStorage.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk error"));
        var sut = ShareServiceFactory.Create(factory, fileStorageService: fileStorage.Object);

        var deleted = await sut.DeleteShareItemsAsync([item.Id]);

        deleted.Should().Be(1);
        // 行已删（用户视角消息已消失），失败的文件路径进了队列而不是只记一条日志
        // 全新上下文验证：播种用的上下文仍跟踪着旧实体，会返回过期值。
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.CountAsync()).Should().Be(0);
        var queued = await verifyDb.PendingFileDeletions.OrderBy(x => x.StoragePath).ToListAsync();
        queued.Should().HaveCount(2, "原文件与缩略图各记一条");
        queued.Select(x => x.StoragePath).Should().BeEquivalentTo(
            "20261114/deadbeef.png", "thumbnails/deadbeef.jpg");
        queued.Should().OnlyContain(x => x.ShareItemId == item.Id);
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_MarksForDelayedDeletion_AndHidesImmediately()
    {
        var factory = CreateInMemoryDbFactory();
        await using var db = factory.CreateDbContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var item = SeedFileItem(db, clock.GetUtcNow().AddMonths(-2));

        var fileStorage = new Mock<IFileStorageService>();
        var sut = ShareServiceFactory.Create(factory, fileStorageService: fileStorage.Object, timeProvider: clock);

        var marked = await sut.CleanupOldMessagesAsync(1);

        marked.Should().Be(1);
        // 常规读路径已看不到（全局查询过滤器），但行仍在库里，带 7 天后的标记
        // 全新上下文验证：播种用的上下文仍跟踪着旧实体，会返回过期值。
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.CountAsync()).Should().Be(0);
        var hidden = await verifyDb.ShareItems.IgnoreQueryFilters().SingleAsync();
        hidden.DeleteAfter.Should().Be(clock.GetUtcNow().AddDays(DeletionPolicy.DelayedDeleteDays));
        // 批量清理不再立即删文件：清扫任务到期后才处理
        fileStorage.Verify(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_DoesNotReMarkAlreadyMarked()
    {
        var factory = CreateInMemoryDbFactory();
        await using var db = factory.CreateDbContext();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        SeedFileItem(db, clock.GetUtcNow().AddMonths(-2));
        var sut = ShareServiceFactory.Create(factory, timeProvider: clock);

        (await sut.CleanupOldMessagesAsync(1)).Should().Be(1);
        (await sut.CleanupOldMessagesAsync(1)).Should().Be(0, "已标记的消息不再进入清理查询");
    }

    /// <summary>
    /// S0-08（DAT-01）：模拟进程在「DB 提交之后、删文件之前」被 kill。
    /// 真正的 kill -9 无法在单元测试里复现（会杀掉测试进程本身），这里用
    /// 存储层在 DeleteFileAsync 抛错来模拟同一崩溃点：DB 已提交，文件还在。
    /// 验收点是重启后零悬空引用（行不在、文件成孤儿而非悬空指针，
    /// 孤儿由待删除队列在 Stage 1 消费）。
    /// </summary>
    [Fact]
    public async Task KillMidDelete_SimulatedCrash_LeavesNoDanglingReferences()
    {
        var dbName = $"anydrop-kill-sim-{Guid.NewGuid():N}";
        DbContextOptions<AnyDropDbContext> Options() => new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase(dbName).Options;

        var storageDir = Path.Combine(Path.GetTempPath(), "anydrop-kill-sim", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageDir);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:BasePath"] = storageDir
                })
                .Build();
            var realStorage = new LocalFileStorageService(configuration, TimeProvider.System);
            var storedPath = await realStorage.SaveFileAsync(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes("victim")),
                "victim.txt", "text/plain");

            Guid itemId;
            await using (var db = new AnyDropDbContext(Options()))
            {
                var item = new ShareItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = ShareContentType.File,
                    Content = storedPath,
                    FileName = "victim.txt",
                    MimeType = "text/plain",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.ShareItems.Add(item);
                await db.SaveChangesAsync();
                itemId = item.Id;
            }

            // 存储层在删文件时抛错 = 进程在 DB 提交后、文件删除前被 kill
            var crashingStorage = new Mock<IFileStorageService>();
            crashingStorage.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("simulated crash between DB commit and file delete"));

            // 服务用工厂建自己的上下文（与播种用的上下文共享同一个 InMemory 库名）。
            var sut = ShareServiceFactory.Create(new TestDbContextFactory(Options()), fileStorageService: crashingStorage.Object);
            (await sut.DeleteShareItemsAsync([itemId])).Should().Be(1);

            // "重启"：全新的 DbContext 读库
            await using var freshDb = new AnyDropDbContext(Options());
            (await freshDb.ShareItems.CountAsync()).Should().Be(0, "DB 先提交：重启后没有任何悬空引用");
            var queued = await freshDb.PendingFileDeletions.ToListAsync();
            queued.Should().ContainSingle(x => x.ShareItemId == itemId && x.StoragePath == storedPath);
            // 文件还在磁盘上（孤儿文件，不是悬空引用；Stage 1 的队列消费负责重试）
            File.Exists(Path.Combine(storageDir, storedPath.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(storageDir))
            {
                Directory.Delete(storageDir, recursive: true);
            }
        }
    }

    /// <summary>迁移确实建出了 DeleteAfter 列与 PendingFileDeletions 表。</summary>
    [Fact]
    public async Task Migration_CreatesDeleteAfterColumnAndQueueTable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        await using var db = new AnyDropDbContext(options);
        await db.Database.MigrateAsync();

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var columnCommand = connection.CreateCommand();
        columnCommand.CommandText = "PRAGMA table_info(ShareItems);";
        await using var columnReader = await columnCommand.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await columnReader.ReadAsync())
        {
            columns.Add(columnReader["name"]?.ToString() ?? string.Empty);
        }

        columns.Should().Contain("DeleteAfter");

        await columnReader.CloseAsync();

        await using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'PendingFileDeletions';";
        Convert.ToInt32(await tableCommand.ExecuteScalarAsync()).Should().Be(1);
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
