using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S1-01（DAT-02）回归组：预占位上传 + 对账清扫（超期预占位 / 延迟删除 / 待删队列）
/// + 缺失引用修复记录。
/// </summary>
public class OrphanGovernanceTests : IDisposable
{
    private readonly string _basePath =
        Path.Combine(AppContext.BaseDirectory, "test-governance", Guid.NewGuid().ToString("N"));

    private static TestDbContextFactory CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-governance-{Guid.NewGuid():N}")
            .Options;

        return new TestDbContextFactory(options);
    }

    private IConfiguration CreateConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = _basePath
            })
            .Build();

    private OrphanFileReconciler CreateSut(
        TestDbContextFactory factory,
        IFileStorageService? storageService = null,
        TimeProvider? timeProvider = null)
    {
        var configuration = CreateConfiguration();
        return new OrphanFileReconciler(
            factory,
            configuration,
            storageService ?? new LocalFileStorageService(configuration, timeProvider ?? TimeProvider.System),
            timeProvider ?? TimeProvider.System,
            NullLogger<OrphanFileReconciler>.Instance);
    }

    private string WriteAgedFile(string relativePath, string content = "x")
    {
        var full = Path.Combine(_basePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddDays(-2));
        return full;
    }

    private static ShareItem FileItem(string content, string? thumbnailPath = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.File,
            Content = content,
            ThumbnailPath = thumbnailPath,
            CreatedAt = DateTimeOffset.UtcNow
        };

    // ── 预占位上传 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendFileAsync_WhenSaveFails_RemovesPlaceholderAndThrows()
    {
        var factory = CreateDbContextFactory();
        var storageMock = new Mock<IFileStorageService>();
        storageMock.Setup(x => x.GenerateStoragePath(It.IsAny<string>())).Returns("saved/x.bin");
        storageMock.Setup(x => x.SaveFileAtPathAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("模拟落盘失败"));
        var service = ShareServiceFactory.Create(factory, fileStorageService: storageMock.Object);

        await using var stream = new MemoryStream([1, 2, 3]);
        var act = () => service.SendFileAsync(stream, "x.bin", "application/octet-stream");

        await act.Should().ThrowAsync<IOException>();

        // 预占位行已被删掉，不留 Uploading 残留
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SendFileAsync_Success_RowIsReadyAndVisible()
    {
        var factory = CreateDbContextFactory();
        var storageMock = new Mock<IFileStorageService>();
        storageMock.Setup(x => x.GenerateStoragePath(It.IsAny<string>())).Returns("saved/x.bin");
        storageMock.Setup(x => x.SaveFileAtPathAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string path, string _, CancellationToken _) => path);
        var service = ShareServiceFactory.Create(factory, fileStorageService: storageMock.Object);

        await using var stream = new MemoryStream([1, 2, 3]);
        var dto = await service.SendFileAsync(stream, "x.bin", "application/octet-stream");

        dto.Content.Should().Be("saved/x.bin");
        await using var verifyDb = factory.CreateDbContext();
        var entity = await verifyDb.ShareItems.IgnoreQueryFilters().SingleAsync();
        entity.UploadState.Should().Be(UploadState.Ready);
        (await verifyDb.ShareItems.CountAsync()).Should().Be(1, "转正后的行应对常规读路径可见");
    }

    [Fact]
    public async Task GetRecentAsync_HidesUploadingPlaceholder()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.ShareItems.Add(new ShareItem
        {
            ContentType = ShareContentType.File,
            Content = "20260101/uploading.bin",
            FileName = "uploading.bin",
            UploadState = UploadState.Uploading,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.ShareItems.Add(new ShareItem
        {
            ContentType = ShareContentType.Text,
            Content = "visible",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = ShareServiceFactory.Create(factory);
        var recent = await service.GetRecentAsync(10);

        recent.Select(x => x.Content).Should().Equal("visible");
    }

    // ── 超期预占位清扫 ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_StalePlaceholder_RemovesRowAndFile()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.ShareItems.Add(new ShareItem
        {
            ContentType = ShareContentType.File,
            Content = "20260101/stale.bin",
            FileName = "stale.bin",
            UploadState = UploadState.Uploading,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        });
        await db.SaveChangesAsync();
        var fullPath = WriteAgedFile("20260101/stale.bin");

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        result.StalePlaceholdersRemoved.Should().Be(1);
        File.Exists(fullPath).Should().BeFalse();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReconcileAsync_FreshPlaceholder_Kept()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.ShareItems.Add(new ShareItem
        {
            ContentType = ShareContentType.File,
            Content = "20260101/fresh.bin",
            FileName = "fresh.bin",
            UploadState = UploadState.Uploading,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();
        var fullPath = WriteAgedFile("20260101/fresh.bin");

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        result.StalePlaceholdersRemoved.Should().Be(0);
        File.Exists(fullPath).Should().BeTrue();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // ── 延迟删除清扫（D5）───────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_ExpiredDelete_SweepsRowAndFile()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = FileItem("20260101/expired.bin");
        item.DeleteAfter = DateTimeOffset.UtcNow.AddHours(-1);
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();
        var fullPath = WriteAgedFile("20260101/expired.bin");

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        result.SweepRowsRemoved.Should().Be(1);
        result.SweepFilesDeleted.Should().Be(1);
        File.Exists(fullPath).Should().BeFalse();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReconcileAsync_ExpiredDelete_NotYetDue_Kept()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = FileItem("20260101/notdue.bin");
        item.DeleteAfter = DateTimeOffset.UtcNow.AddDays(6);
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();
        var fullPath = WriteAgedFile("20260101/notdue.bin");

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        result.SweepRowsRemoved.Should().Be(0);
        File.Exists(fullPath).Should().BeTrue();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReconcileAsync_ExpiredDelete_SharedPath_FileKeptForLiveRow()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var marked = FileItem("20260101/shared.bin");
        marked.DeleteAfter = DateTimeOffset.UtcNow.AddHours(-1);
        var live = FileItem("20260101/shared.bin");
        db.ShareItems.AddRange(marked, live);
        await db.SaveChangesAsync();
        var fullPath = WriteAgedFile("20260101/shared.bin");

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        result.SweepRowsRemoved.Should().Be(1);
        result.SweepFilesDeleted.Should().Be(0, "另一活行仍引用同一路径，文件不能删");
        File.Exists(fullPath).Should().BeTrue();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // ── 待删队列消费 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_QueueDrain_RemovesEntryForMissingFile()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.PendingFileDeletions.Add(new PendingFileDeletion
        {
            StoragePath = "20260101/gone.bin",
            Reason = "manual-delete",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        // 目录必须存在：目录不存在时对账认为是卷未挂载，会直接跳过清扫与队列消费
        // （分不清「文件真没了」还是「卷没挂上」，后者绝不能撤队）。
        Directory.CreateDirectory(Path.Combine(_basePath, "20260101"));

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        result.QueueEntriesDrained.Should().Be(1);
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.PendingFileDeletions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReconcileAsync_BasePathMissing_QueueNotDrained()
    {
        // 目录不存在 = 卷可能没挂上：此时「文件不存在」不可信，
        // 绝不能撤队（否则卷挂回来后该删的文件就没人管了）。
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.PendingFileDeletions.Add(new PendingFileDeletion
        {
            StoragePath = "20260101/maybe-there.bin",
            Reason = "manual-delete",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        result.QueueEntriesDrained.Should().Be(0);
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.PendingFileDeletions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReconcileAsync_QueueDrain_SkipsLiveReferencedFile()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.ShareItems.Add(FileItem("20260101/live.bin"));
        db.PendingFileDeletions.Add(new PendingFileDeletion
        {
            StoragePath = "20260101/live.bin",
            Reason = "stale-entry",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        var fullPath = WriteAgedFile("20260101/live.bin");

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        // 队列记录过期撤队，但活文件永不删除
        result.QueueEntriesDrained.Should().Be(1);
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task ReconcileAsync_QueueDrain_RetriesWithBackoff()
    {
        var start = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.PendingFileDeletions.Add(new PendingFileDeletion
        {
            StoragePath = "20260101/stuck.bin",
            Reason = "manual-delete",
            CreatedAt = start.AddHours(-1.5)
        });
        await db.SaveChangesAsync();
        WriteAgedFile("20260101/stuck.bin");

        var storageMock = new Mock<IFileStorageService>();
        storageMock.Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("模拟删除失败"));
        var sut = CreateSut(factory, storageService: storageMock.Object, timeProvider: clock);

        // 第一次：入队 1.5 小时 > 首次退避 1 小时，尝试并失败，RetryCount=1
        var first = await sut.ReconcileAsync(deleteOrphans: false);
        first.QueueEntriesFailed.Should().Be(1);
        await using (var verifyDb = factory.CreateDbContext())
        {
            var entry = await verifyDb.PendingFileDeletions.SingleAsync();
            entry.RetryCount.Should().Be(1);
        }

        // 立即再跑：退避升到 2 小时（入队 1.5 小时 < 2 小时），跳过不尝试
        var second = await sut.ReconcileAsync(deleteOrphans: false);
        second.QueueEntriesFailed.Should().Be(0);
        await using (var verifyDb = factory.CreateDbContext())
        {
            var entry = await verifyDb.PendingFileDeletions.SingleAsync();
            entry.RetryCount.Should().Be(1);
        }

        // 推进 1 小时（入队 2.5 小时 > 退避 2 小时）：再次尝试，再次失败
        clock.Advance(TimeSpan.FromHours(1));
        var third = await sut.ReconcileAsync(deleteOrphans: false);
        third.QueueEntriesFailed.Should().Be(1);
        await using (var verifyDb = factory.CreateDbContext())
        {
            var entry = await verifyDb.PendingFileDeletions.SingleAsync();
            entry.RetryCount.Should().Be(2);
        }
    }

    // ── 缺失引用修复记录 ────────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_MissingReference_CreatesReport()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = FileItem("20260101/missing.bin");
        item.FileName = "missing.bin";
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: false);

        result.MissingReferencedFiles.Should().Be(1);
        result.MissingReportsOpen.Should().Be(1);
        await using var verifyDb = factory.CreateDbContext();
        var report = await verifyDb.MissingFileReports.SingleAsync();
        report.ShareItemId.Should().Be(item.Id);
        report.StoragePath.Should().Be("20260101/missing.bin");
        report.Kind.Should().Be("content");
        report.IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileAsync_MissingReference_FileRestored_AutoResolves()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.ShareItems.Add(FileItem("20260101/back.bin"));
        await db.SaveChangesAsync();

        var sut = CreateSut(factory);
        var first = await sut.ReconcileAsync(deleteOrphans: false);
        first.MissingReportsOpen.Should().Be(1);

        // 文件恢复了
        WriteAgedFile("20260101/back.bin");
        var second = await sut.ReconcileAsync(deleteOrphans: false);

        second.MissingReportsOpen.Should().Be(0);
        await using var verifyDb = factory.CreateDbContext();
        var report = await verifyDb.MissingFileReports.SingleAsync();
        report.IsResolved.Should().BeTrue();
        report.ResolvedBy.Should().Be("auto");
    }

    [Fact]
    public async Task ReconcileAsync_MissingReference_SecondScan_DoesNotDuplicateReport()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        db.ShareItems.Add(FileItem("20260101/still-missing.bin"));
        await db.SaveChangesAsync();

        var sut = CreateSut(factory);
        await sut.ReconcileAsync(deleteOrphans: false);
        var second = await sut.ReconcileAsync(deleteOrphans: false);

        second.MissingReportsOpen.Should().Be(1);
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.MissingFileReports.CountAsync()).Should().Be(1, "同一缺失只记一条未解决记录");
    }

    [Fact]
    public async Task ReconcileAsync_OrphanDeleteEnabled_DeletesAgedOrphan()
    {
        var factory = CreateDbContextFactory();
        var fullPath = WriteAgedFile("20260101/orphan.bin");

        var sut = CreateSut(factory);
        var result = await sut.ReconcileAsync(deleteOrphans: true);

        result.OrphansFound.Should().Be(1);
        result.OrphansDeleted.Should().Be(1);
        File.Exists(fullPath).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, true);
        }
    }
}
