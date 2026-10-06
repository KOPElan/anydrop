using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// 存储目录与数据库双向对账的测试。
///
/// 重点是「不能误删」：引用集合必须同时包含原文件与缩略图，
/// 且宽限期内的文件（可能正在上传）必须被跳过。
/// </summary>
public class OrphanFileReconcilerTests : IDisposable
{
    private readonly string _basePath =
        Path.Combine(AppContext.BaseDirectory, "test-orphan", Guid.NewGuid().ToString("N"));

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-orphan-{Guid.NewGuid():N}")
            .Options;

        return new AnyDropDbContext(options);
    }

    private OrphanFileReconciler CreateSut(AnyDropDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = _basePath
            })
            .Build();

        return new OrphanFileReconciler(
            db, configuration, TimeProvider.System, NullLogger<OrphanFileReconciler>.Instance);
    }

    /// <summary>写入一个「足够旧」的文件，使其超出 24 小时宽限期。</summary>
    private string WriteAgedFile(string relativePath, string content = "x")
    {
        var full = Path.Combine(_basePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddDays(-2));
        return full;
    }

    private string WriteFreshFile(string relativePath, string content = "x")
    {
        var full = Path.Combine(_basePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
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

    [Fact]
    public async Task ReconcileAsync_OrphanFile_IsReportedButKeptByDefault()
    {
        await using var db = CreateDbContext();
        db.ShareItems.Add(FileItem("20260101/keep.bin"));
        await db.SaveChangesAsync();

        var kept = WriteAgedFile("20260101/keep.bin");
        var orphan = WriteAgedFile("20260101/orphan.bin", "orphan-content");

        var result = await CreateSut(db).ReconcileAsync(deleteOrphans: false);

        result.FilesScanned.Should().Be(2);
        result.OrphansFound.Should().Be(1);
        result.OrphansDeleted.Should().Be(0);
        result.OrphanBytes.Should().Be(new FileInfo(orphan).Length);
        result.SampleOrphans.Should().ContainSingle().Which.Should().Be("20260101/orphan.bin");
        File.Exists(kept).Should().BeTrue();
        File.Exists(orphan).Should().BeTrue("默认只报告，不删除");
    }

    [Fact]
    public async Task ReconcileAsync_WithDeleteEnabled_RemovesOnlyOrphans()
    {
        await using var db = CreateDbContext();
        db.ShareItems.Add(FileItem("20260101/keep.bin"));
        await db.SaveChangesAsync();

        var kept = WriteAgedFile("20260101/keep.bin");
        var orphan = WriteAgedFile("20260101/orphan.bin");

        var result = await CreateSut(db).ReconcileAsync(deleteOrphans: true);

        result.OrphansDeleted.Should().Be(1);
        File.Exists(kept).Should().BeTrue("被引用的文件绝不能删");
        File.Exists(orphan).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileAsync_ThumbnailPath_CountsAsReferenced()
    {
        // 缩略图必须计入引用集合，否则会被当成孤儿误删
        await using var db = CreateDbContext();
        db.ShareItems.Add(FileItem("20260101/a.png", thumbnailPath: "thumbnails/a.jpg"));
        await db.SaveChangesAsync();

        var thumbnail = WriteAgedFile("thumbnails/a.jpg");
        var content = WriteAgedFile("20260101/a.png");

        var result = await CreateSut(db).ReconcileAsync(deleteOrphans: true);

        result.OrphansFound.Should().Be(0);
        File.Exists(thumbnail).Should().BeTrue();
        File.Exists(content).Should().BeTrue();
    }

    [Fact]
    public async Task ReconcileAsync_RecentUnreferencedFile_IsSkippedByGracePeriod()
    {
        await using var db = CreateDbContext();

        // 刚写入、尚未被引用的文件可能正在上传中，不能判为孤儿
        var inFlight = WriteFreshFile("20260101/inflight.bin");

        var result = await CreateSut(db).ReconcileAsync(deleteOrphans: true);

        result.OrphansFound.Should().Be(0);
        File.Exists(inFlight).Should().BeTrue();
    }

    [Fact]
    public async Task ReconcileAsync_AgedTempFileFromCrash_IsReclaimed()
    {
        await using var db = CreateDbContext();

        // 原子写入的临时文件；崩溃残留的应被回收
        var staleTemp = WriteAgedFile("20260101/.abc123.part", "half-written");

        var result = await CreateSut(db).ReconcileAsync(deleteOrphans: true);

        result.OrphansFound.Should().Be(1);
        File.Exists(staleTemp).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileAsync_MissingReferencedFile_IsCountedButNotDeleted()
    {
        await using var db = CreateDbContext();
        db.ShareItems.Add(FileItem("20260101/gone.bin"));
        await db.SaveChangesAsync();

        var result = await CreateSut(db).ReconcileAsync(deleteOrphans: true);

        result.MissingReferencedFiles.Should().Be(1);
        result.OrphansFound.Should().Be(0);
    }

    [Fact]
    public async Task ReconcileAsync_TextAndLinkItems_AreNotTreatedAsFileReferences()
    {
        await using var db = CreateDbContext();
        db.ShareItems.Add(new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.Text,
            Content = "20260101/not-a-file.bin",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        // 文本消息的 Content 是正文，不是存储路径，因此不应保护同名文件
        var file = WriteAgedFile("20260101/not-a-file.bin");

        var result = await CreateSut(db).ReconcileAsync(deleteOrphans: true);

        result.OrphansFound.Should().Be(1);
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public async Task ReconcileAsync_WhenStorageDirectoryMissing_ReturnsEmptyResult()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.ReconcileAsync(deleteOrphans: true);

        result.FilesScanned.Should().Be(0);
        result.OrphansFound.Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }
}
