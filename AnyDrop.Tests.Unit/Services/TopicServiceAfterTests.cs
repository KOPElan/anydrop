using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>S2-04：服务端增量拉取（after 参数）——重连补拉的服务端支点。</summary>
public class TopicServiceAfterTests
{
    [Fact]
    public async Task GetTopicMessagesAsync_WithAfter_ReturnsOnlyNewerInAscendingOrder()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var topic = new Topic { Name = "A" };
        dbContext.Topics.Add(topic);
        var now = DateTimeOffset.UtcNow;
        dbContext.ShareItems.AddRange(
            new ShareItem { Content = "m1", TopicId = topic.Id, CreatedAt = now.AddMinutes(-3), ContentType = ShareContentType.Text },
            new ShareItem { Content = "m2", TopicId = topic.Id, CreatedAt = now.AddMinutes(-2), ContentType = ShareContentType.Text },
            new ShareItem { Content = "m3", TopicId = topic.Id, CreatedAt = now.AddMinutes(-1), ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var result = await service.GetTopicMessagesAsync(topic.Id, 50, null, now.AddMinutes(-2));

        result.Should().NotBeNull();
        // after 用 >=：边界消息 m2 会重复返回一次，调用方按 Id 去重；此处断言包含关系。
        result!.Messages.Select(x => x.Content).Should().Equal("m2", "m3");
    }

    [Fact]
    public async Task GetTopicMessagesAsync_WithAfter_SameTickMessagesAreNotLostAtPageBoundary()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var topic = new Topic { Name = "A" };
        dbContext.Topics.Add(topic);
        var sameTick = DateTimeOffset.UtcNow;
        dbContext.ShareItems.AddRange(
            new ShareItem { Content = "a", TopicId = topic.Id, CreatedAt = sameTick, ContentType = ShareContentType.Text },
            new ShareItem { Content = "b", TopicId = topic.Id, CreatedAt = sameTick, ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var result = await service.GetTopicMessagesAsync(topic.Id, 50, null, sameTick);

        // 严格 > 会在同一刻度处丢消息；>= 保证两条都在（调用方去重边界条）。
        result!.Messages.Select(x => x.Content).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task GetTopicMessagesAsync_WithAfter_ExcludesExpiredMessages()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var topic = new Topic { Name = "A" };
        dbContext.Topics.Add(topic);
        var now = DateTimeOffset.UtcNow;
        dbContext.ShareItems.AddRange(
            new ShareItem { Content = "old", TopicId = topic.Id, CreatedAt = now.AddMinutes(-2), ContentType = ShareContentType.Text },
            new ShareItem { Content = "expired-new", TopicId = topic.Id, CreatedAt = now.AddMinutes(-1), ContentType = ShareContentType.Text, ExpiresAt = now.AddSeconds(-1) },
            new ShareItem { Content = "fresh", TopicId = topic.Id, CreatedAt = now, ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var result = await service.GetTopicMessagesAsync(topic.Id, 50, null, now.AddMinutes(-3));

        result!.Messages.Select(x => x.Content).Should().Equal("old", "fresh");
    }

    [Fact]
    public async Task GetTopicMessagesAsync_WithAfter_NoNewMessages_ReturnsEmpty()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var topic = new Topic { Name = "A" };
        dbContext.Topics.Add(topic);
        var now = DateTimeOffset.UtcNow;
        dbContext.ShareItems.Add(
            new ShareItem { Content = "m1", TopicId = topic.Id, CreatedAt = now.AddMinutes(-1), ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var result = await service.GetTopicMessagesAsync(topic.Id, 50, null, now);

        result!.Messages.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetTopicMessagesAsync_WithAfter_UnknownTopic_ReturnsNull()
    {
        var factory = CreateDbContextFactory();
        var service = CreateService(factory, out _);

        var result = await service.GetTopicMessagesAsync(Guid.NewGuid(), 50, null, DateTimeOffset.UtcNow);

        result.Should().BeNull();
    }

    private static TestDbContextFactory CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"topic-after-tests-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static TopicService CreateService(TestDbContextFactory factory, out Mock<IHubClients> clientsMock)
    {
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        var hubContextMock = new Mock<IHubContext<ShareHub>>();
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        return new TopicService(factory, hubContextMock.Object);
    }
}
