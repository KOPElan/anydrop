using System.Text;
using AnyDrop.Api;
using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S0-06（DAT-05/06，D1）：过期消息读过滤 + 清理广播。
/// 核心语义：ExpiresAt 一到，消息立即对所有读路径不可见，不等下一轮清理；
/// 清理任务标记后广播 ShareItemsDeleted，已打开的页面同步消失。
/// </summary>
public class ExpiredMessageFilteringTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly string _basePath =
        Path.Combine(AppContext.BaseDirectory, "test-expired", Guid.NewGuid().ToString("N"));

    private AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-expired-{Guid.NewGuid():N}")
            .Options;
        return new AnyDropDbContext(options);
    }

    private static async Task<Guid> SeedTopicAsync(AnyDropDbContext db)
    {
        var topicId = Guid.NewGuid();
        db.Topics.Add(new Topic { Id = topicId, Name = "topic", CreatedAt = Now });
        await db.SaveChangesAsync();
        return topicId;
    }

    private static async Task<Guid> SeedTextAsync(
        AnyDropDbContext db, Guid topicId, string content, DateTimeOffset? expiresAt)
    {
        var item = new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.Text,
            Content = content,
            TopicId = topicId,
            CreatedAt = Now,
            ExpiresAt = expiresAt
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    [Fact]
    public async Task GetRecentAsync_ExcludesExpiredImmediately()
    {
        await using var db = CreateDbContext();
        var topicId = await SeedTopicAsync(db);
        await SeedTextAsync(db, topicId, "expired", Now.AddMinutes(-1));
        await SeedTextAsync(db, topicId, "alive", Now.AddMinutes(10));
        await SeedTextAsync(db, topicId, "forever", null);
        var sut = ShareServiceFactory.Create(db, timeProvider: new FakeTimeProvider(Now));

        var recent = await sut.GetRecentAsync();

        recent.Select(x => x.Content).Should().BeEquivalentTo("alive", "forever");
    }

    [Fact]
    public async Task SearchTopicMessagesAsync_ExcludesExpired()
    {
        await using var db = CreateDbContext();
        var topicId = await SeedTopicAsync(db);
        await SeedTextAsync(db, topicId, "hello expired", Now.AddMinutes(-1));
        await SeedTextAsync(db, topicId, "hello alive", Now.AddMinutes(10));
        var sut = ShareServiceFactory.Create(db, timeProvider: new FakeTimeProvider(Now));

        var result = await sut.SearchTopicMessagesAsync(topicId, "hello");

        result.Messages.Select(x => x.Content).Should().ContainSingle().Which.Should().Be("hello alive");
    }

    [Fact]
    public async Task GetTopicMessagesByDate_And_ByType_ExcludeExpired()
    {
        await using var db = CreateDbContext();
        var topicId = await SeedTopicAsync(db);
        await SeedTextAsync(db, topicId, "expired", Now.AddMinutes(-1));
        await SeedTextAsync(db, topicId, "alive", Now.AddMinutes(10));
        var sut = ShareServiceFactory.Create(db, timeProvider: new FakeTimeProvider(Now));

        var byDate = await sut.GetTopicMessagesByDateAsync(topicId, DateOnly.FromDateTime(Now.DateTime), TimeZoneInfo.Utc);
        byDate.Select(x => x.Content).Should().ContainSingle().Which.Should().Be("alive");

        var byType = await sut.GetTopicMessagesByTypeAsync(topicId, ShareContentType.Text);
        byType.Messages.Select(x => x.Content).Should().ContainSingle().Which.Should().Be("alive");

        var dates = await sut.GetTopicActiveDatesAsync(topicId, DateOnly.FromDateTime(Now.DateTime), DateOnly.FromDateTime(Now.DateTime), TimeZoneInfo.Utc);
        dates.Should().ContainSingle().Which.Should().Be(DateOnly.FromDateTime(Now.DateTime));
    }

    [Fact]
    public async Task TopicService_GetTopicMessagesAsync_ExcludesExpired_AndCountsExcludeExpired()
    {
        // 注意：TopicService 直接用 DateTimeOffset.UtcNow（无 TimeProvider 注入），
        // 因此这里按真实当前时间 seeding，不能用上面固定的假 Now。
        await using var db = CreateDbContext();
        var topicId = await SeedTopicAsync(db);
        var realNow = DateTimeOffset.UtcNow;
        await SeedTextAsync(db, topicId, "expired", realNow.AddMinutes(-1));
        await SeedTextAsync(db, topicId, "alive", realNow.AddMinutes(10));

        var hubContext = new Mock<IHubContext<ShareHub>>();
        var sut = new TopicService(db, hubContext.Object);

        var result = await sut.GetTopicMessagesAsync(topicId, 50, null);
        result.Should().NotBeNull();
        result!.Messages.Select(x => x.Content).Should().ContainSingle().Which.Should().Be("alive");
        result.HasMore.Should().BeFalse();

        var topic = await sut.GetTopicByIdAsync(topicId);
        topic!.MessageCount.Should().Be(1);
    }

    [Fact]
    public async Task GetFileAsync_ExpiredItem_ReturnsNotFound()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:BasePath"] = _basePath })
            .Build();
        var storage = new LocalFileStorageService(configuration, TimeProvider.System);
        await using var db = CreateDbContext();
        var stored = await storage.SaveFileAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("secret")), "secret.txt", "text/plain");
        var item = new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.File,
            Content = stored,
            FileName = "secret.txt",
            MimeType = "text/plain",
            CreatedAt = Now,
            ExpiresAt = Now.AddMinutes(-1)
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var result = await ShareItemEndpoints.GetFileAsync(item.Id, null, db, storage, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound<ApiEnvelope<object>>>();
    }

    [Fact]
    public async Task GetThumbnailAsync_ExpiredItem_ReturnsNotFound()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:BasePath"] = _basePath })
            .Build();
        var storage = new LocalFileStorageService(configuration, TimeProvider.System);
        await using var db = CreateDbContext();
        var stored = await storage.SaveFileAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("thumb")), "thumb.jpg", "image/jpeg");
        var item = new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.Image,
            Content = "20261114/x.png",
            ThumbnailPath = stored,
            MimeType = "image/png",
            CreatedAt = Now,
            ExpiresAt = Now.AddMinutes(-1)
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var result = await ShareItemEndpoints.GetThumbnailAsync(item.Id, db, storage, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound<ApiEnvelope<object>>>();
    }

    [Fact]
    public async Task CleanupExpiredAsync_MarksAndBroadcastsShareItemsDeleted()
    {
        await using var db = CreateDbContext();
        var topicId = await SeedTopicAsync(db);
        var expiredId = await SeedTextAsync(db, topicId, "expired", Now.AddMinutes(-1));
        await SeedTextAsync(db, topicId, "alive", Now.AddMinutes(10));

        var clientProxy = new Mock<IClientProxy>();
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<ShareHub>>();
        hubContext.Setup(c => c.Clients).Returns(hubClients.Object);

        var services = new ServiceCollection();
        services.AddSingleton(new FakeTimeProvider(Now));
        services.AddSingleton(new DbWriteGate());
        services.AddSingleton(hubContext.Object);
        services.AddSingleton<ITopicService>(_ => new TopicService(db, hubContext.Object));
        services.AddSingleton(NullLogger<ExpiredMessageCleanupService>.Instance);
        // 清理服务用 CreateAsyncScope 解析 DbContext；这里直接注册 seed 用的同一个实例。
        services.AddSingleton(db);
        services.AddSingleton<AnyDropDbContext>(_ => db);
        var provider = services.BuildServiceProvider();
        var sut = new ExpiredMessageCleanupService(
            provider, new FakeTimeProvider(Now), new DbWriteGate(),
            NullLogger<ExpiredMessageCleanupService>.Instance);

        await sut.CleanupExpiredAsync(CancellationToken.None);

        // 已标记（DeleteAfter 有值），常规查询已看不到
        var marked = await db.ShareItems.IgnoreQueryFilters().SingleAsync(x => x.Id == expiredId);
        marked.DeleteAfter.Should().NotBeNull();
        (await db.ShareItems.CountAsync()).Should().Be(1);

        // 广播了 ShareItemsDeleted，且参数里包含过期消息 ID
        clientProxy.Verify(c => c.SendCoreAsync(
            "ShareItemsDeleted",
            It.Is<object?[]>(args => BroadcastContainsId(args, expiredId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Moq 的 It.Is 表达式树里不能用 is 模式匹配，抽出来写。
    private static bool BroadcastContainsId(object?[] args, Guid id)
        => args.Length == 1 && args[0] is List<Guid> ids && ids.Contains(id);

    [Fact]
    public async Task DeleteTopicAsync_UnlinksMarkedMessages()
    {
        await using var db = CreateDbContext();
        var topicId = await SeedTopicAsync(db);
        var itemId = await SeedTextAsync(db, topicId, "old", null);
        // 模拟已被批量清理标记的消息
        var item = await db.ShareItems.IgnoreQueryFilters().SingleAsync(x => x.Id == itemId);
        item.DeleteAfter = Now.AddDays(7);
        await db.SaveChangesAsync();

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(new Mock<IClientProxy>().Object);
        var hubContext = new Mock<IHubContext<ShareHub>>();
        hubContext.Setup(c => c.Clients).Returns(hubClients.Object);
        var sut = new TopicService(db, hubContext.Object);

        var deleted = await sut.DeleteTopicAsync(topicId);

        deleted.Should().BeTrue();
        var unlinked = await db.ShareItems.IgnoreQueryFilters().SingleAsync(x => x.Id == itemId);
        unlinked.TopicId.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }
}
