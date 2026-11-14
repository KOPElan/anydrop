using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    private AnyDropDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-delete-{Guid.NewGuid():N}")
            .Options;
        return new AnyDropDbContext(options);
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
        await using var db = CreateInMemoryDb();
        var item = SeedFileItem(db, DateTimeOffset.UtcNow);
        var sut = ShareServiceFactory.Create(db);

        var deleted = await sut.DeleteShareItemsAsync([item.Id]);

        deleted.Should().Be(1);
        (await db.ShareItems.CountAsync()).Should().Be(0, "数据库行已提交删除");
    }

    [Fact]
    public async Task DeleteShareItemsAsync_FileDeleteFailure_IsQueuedNotLost()
    {
        await using var db = CreateInMemoryDb();
        var item = SeedFileItem(db, DateTimeOffset.UtcNow);

        var fileStorage = new Mock<IFileStorageService>();
        fileStorage.Setup(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk error"));
        var sut = ShareServiceFactory.Create(db, fileStorageService: fileStorage.Object);

        var deleted = await sut.DeleteShareItemsAsync([item.Id]);

        deleted.Should().Be(1);
        // 行已删（用户视角消息已消失），失败的文件路径进了队列而不是只记一条日志
        (await db.ShareItems.CountAsync()).Should().Be(0);
        var queued = await db.PendingFileDeletions.OrderBy(x => x.StoragePath).ToListAsync();
        queued.Should().HaveCount(2, "原文件与缩略图各记一条");
        queued.Select(x => x.StoragePath).Should().BeEquivalentTo(
            "20261114/deadbeef.png", "thumbnails/deadbeef.jpg");
        queued.Should().OnlyContain(x => x.ShareItemId == item.Id);
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_MarksForDelayedDeletion_AndHidesImmediately()
    {
        await using var db = CreateInMemoryDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var item = SeedFileItem(db, clock.GetUtcNow().AddMonths(-2));

        var fileStorage = new Mock<IFileStorageService>();
        var sut = ShareServiceFactory.Create(db, fileStorageService: fileStorage.Object, timeProvider: clock);

        var marked = await sut.CleanupOldMessagesAsync(1);

        marked.Should().Be(1);
        // 常规读路径已看不到（全局查询过滤器），但行仍在库里，带 7 天后的标记
        (await db.ShareItems.CountAsync()).Should().Be(0);
        var hidden = await db.ShareItems.IgnoreQueryFilters().SingleAsync();
        hidden.DeleteAfter.Should().Be(clock.GetUtcNow().AddDays(DeletionPolicy.DelayedDeleteDays));
        // 批量清理不再立即删文件：清扫任务到期后才处理
        fileStorage.Verify(s => s.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_DoesNotReMarkAlreadyMarked()
    {
        await using var db = CreateInMemoryDb();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        SeedFileItem(db, clock.GetUtcNow().AddMonths(-2));
        var sut = ShareServiceFactory.Create(db, timeProvider: clock);

        (await sut.CleanupOldMessagesAsync(1)).Should().Be(1);
        (await sut.CleanupOldMessagesAsync(1)).Should().Be(0, "已标记的消息不再进入清理查询");
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
