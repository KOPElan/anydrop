using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// 用**真实的 SQLite provider** 验证搜索查询。
///
/// 其余测试都基于 InMemory provider：它会在客户端求值，不校验 LINQ 是否能翻译成 SQL，
/// 因此无法发现「查询写法无法翻译」这类只在生产环境（SQLite）暴露的问题。
/// 这里同时验证按文件名搜索确实生效——此前只匹配 Content，
/// 而文件类消息的 Content 是内部存储路径，用户按文件名根本搜不到。
/// </summary>
public class ShareServiceSqliteSearchTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(AppContext.BaseDirectory, "test-sqlite", $"{Guid.NewGuid():N}.db");

    private AnyDropDbContext CreateSqliteContext()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        var db = new AnyDropDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static ShareService CreateService(AnyDropDbContext db)
        => ShareServiceFactory.Create(db);

    private static async Task<Guid> SeedAsync(AnyDropDbContext db)
    {
        var topicId = Guid.NewGuid();
        db.Topics.Add(new Topic { Id = topicId, Name = "topic", CreatedAt = DateTimeOffset.UtcNow });

        // 文件类消息：Content 是内部存储路径，FileName 才是用户可见的名字
        db.ShareItems.Add(new ShareItem
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            ContentType = ShareContentType.File,
            Content = "20260418/deadbeefcafe.png",
            FileName = "Quarterly-Report.pdf",
            MimeType = "application/pdf",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // 链接类消息：标题来自 OGP 抓取结果
        db.ShareItems.Add(new ShareItem
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            ContentType = ShareContentType.Link,
            Content = "https://example.com/article",
            LinkTitle = "深入理解 EF Core 查询翻译",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // 纯文本消息
        db.ShareItems.Add(new ShareItem
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            ContentType = ShareContentType.Text,
            Content = "hello world",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        return topicId;
    }

    [Fact]
    public async Task SearchTopicMessagesAsync_TranslatesToSqliteAndMatchesFileName()
    {
        // 若 ToLowerInvariant / 多列 OR 的写法无法翻译成 SQL，这一步会直接抛异常
        await using var db = CreateSqliteContext();
        var topicId = await SeedAsync(db);
        var service = CreateService(db);

        var byFileName = await service.SearchTopicMessagesAsync(topicId, "Quarterly-Report");

        byFileName.Messages.Should().ContainSingle();
        byFileName.Messages[0].FileName.Should().Be("Quarterly-Report.pdf");
    }

    [Fact]
    public async Task SearchTopicMessagesAsync_IsCaseInsensitive()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedAsync(db);
        var service = CreateService(db);

        var result = await service.SearchTopicMessagesAsync(topicId, "QUARTERLY-report");

        result.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchTopicMessagesAsync_MatchesLinkTitle()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedAsync(db);
        var service = CreateService(db);

        var result = await service.SearchTopicMessagesAsync(topicId, "查询翻译");

        result.Messages.Should().ContainSingle();
        result.Messages[0].LinkTitle.Should().Be("深入理解 EF Core 查询翻译");
    }

    [Fact]
    public async Task SearchTopicMessagesAsync_StillMatchesPlainTextContent()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedAsync(db);
        var service = CreateService(db);

        var result = await service.SearchTopicMessagesAsync(topicId, "hello");

        result.Messages.Should().ContainSingle();
        result.Messages[0].Content.Should().Be("hello world");
    }

    [Fact]
    public async Task SearchTopicMessagesAsync_WithNoMatch_ReturnsEmpty()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedAsync(db);
        var service = CreateService(db);

        var result = await service.SearchTopicMessagesAsync(topicId, "zzz-not-present-zzz");

        result.Messages.Should().BeEmpty();
    }

    public void Dispose()
    {
        // SQLite 会创建连接池，先清空再删文件
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        var dir = Path.GetDirectoryName(_dbPath);
        if (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
        }
    }
}
