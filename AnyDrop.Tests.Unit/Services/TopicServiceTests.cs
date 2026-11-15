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

public class TopicServiceTests
{
    [Fact]
    public async Task CreateTopicAsync_WhenNameIsEmpty_ThrowsException()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _);

        var act = () => service.CreateTopicAsync(new CreateTopicRequest(" "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateTopicAsync_WhenNameIsValid_ReturnsTopicDto()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _);

        var result = await service.CreateTopicAsync(new CreateTopicRequest("工作"));

        result.Name.Should().Be("工作");
        result.SortOrder.Should().Be(int.MaxValue);
        (await dbContext.Topics.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetAllTopicsAsync_ReturnsTopicsInCorrectSortOrder()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.Topics.AddRange(
            new Topic { Id = Guid.NewGuid(), Name = "C", SortOrder = 1, CreatedAt = now.AddMinutes(-5), LastMessageAt = now.AddMinutes(-2) },
            new Topic { Id = Guid.NewGuid(), Name = "A", SortOrder = 0, CreatedAt = now.AddMinutes(-10), LastMessageAt = now.AddMinutes(-1) },
            new Topic { Id = Guid.NewGuid(), Name = "B", SortOrder = 1, CreatedAt = now.AddMinutes(-1), LastMessageAt = null });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var result = await service.GetAllTopicsAsync();

        result.Select(x => x.Name).Should().Equal("A", "C", "B");
    }

    [Fact]
    public async Task ReorderTopicsAsync_UpdatesSortOrderInDatabase()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var topicA = new Topic { Name = "A", SortOrder = 0 };
        var topicB = new Topic { Name = "B", SortOrder = 1 };
        dbContext.Topics.AddRange(topicA, topicB);
        await dbContext.SaveChangesAsync();
        var service = CreateService(factory, out _);

        await service.ReorderTopicsAsync(new ReorderTopicsRequest(
        [
            new TopicOrderItem(topicA.Id, 5),
            new TopicOrderItem(topicB.Id, 3)
        ]));

        var refreshed = await dbContext.Topics.AsNoTracking().ToDictionaryAsync(x => x.Id);
        refreshed[topicA.Id].SortOrder.Should().Be(5);
        refreshed[topicB.Id].SortOrder.Should().Be(3);
    }

    [Fact]
    public async Task GetTopicMessagesAsync_WithCursor_ReturnsPaginatedResults()
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
        var first = await service.GetTopicMessagesAsync(topic.Id, 2, null);
        var second = await service.GetTopicMessagesAsync(topic.Id, 2, DateTimeOffset.Parse(first!.NextCursor!));

        first!.Messages.Select(x => x.Content).Should().Equal("m3", "m2");
        first.HasMore.Should().BeTrue();
        second!.Messages.Select(x => x.Content).Should().Equal("m1");
    }

    [Fact]
    public async Task DeleteTopicAsync_SetsTopicIdNullOnMessages()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var topic = new Topic { Name = "A" };
        dbContext.Topics.Add(topic);
        dbContext.ShareItems.Add(new ShareItem
        {
            Content = "hello",
            ContentType = ShareContentType.Text,
            TopicId = topic.Id
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var deleted = await service.DeleteTopicAsync(topic.Id);

        deleted.Should().BeTrue();
        // 全新上下文验证：播种用的上下文仍跟踪着旧实体，会返回过期值。
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Topics.CountAsync()).Should().Be(0);
        (await verifyDb.ShareItems.SingleAsync()).TopicId.Should().BeNull();
    }

    [Fact]
    public async Task TopicMutations_BroadcastTopicsUpdated()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out var clientProxyMock);

        var topic = await service.CreateTopicAsync(new CreateTopicRequest("A"));
        await service.ReorderTopicsAsync(new ReorderTopicsRequest([new TopicOrderItem(topic.Id, 0)]));
        await service.DeleteTopicAsync(topic.Id);

        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                "TopicsUpdated",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(3));
    }

    [Fact]
    public async Task PinTopicAsync_WhenPinned_SetsPinnedFields()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var topic = new Topic { Name = "Pin me" };
        dbContext.Topics.Add(topic);
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var result = await service.PinTopicAsync(topic.Id, true);

        result.IsPinned.Should().BeTrue();
        result.PinnedAt.Should().NotBeNull();

        // 全新上下文验证：播种用的上下文仍跟踪着旧实体，会返回过期值。
        await using var verifyDb = factory.CreateDbContext();
        var entity = await verifyDb.Topics.SingleAsync();
        entity.IsPinned.Should().BeTrue();
        entity.PinnedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllTopicsAsync_WhenPinned_ReturnsPinnedFirst()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.Topics.AddRange(
            new Topic { Name = "Normal", SortOrder = 0, CreatedAt = now, LastMessageAt = now },
            new Topic { Name = "Pinned", SortOrder = 99, CreatedAt = now, LastMessageAt = now.AddMinutes(-10), IsPinned = true, PinnedAt = now.AddMinutes(-20) });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var topics = await service.GetAllTopicsAsync();

        topics[0].Name.Should().Be("Pinned");
        topics[0].IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllTopicsAsync_MultiplePinned_SortsByPinnedAtAscending()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.Topics.AddRange(
            new Topic { Name = "Pinned-Late", SortOrder = 0, IsPinned = true, PinnedAt = now.AddMinutes(-1) },
            new Topic { Name = "Pinned-Early", SortOrder = 0, IsPinned = true, PinnedAt = now.AddMinutes(-10) },
            new Topic { Name = "Normal", SortOrder = 0 });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _);
        var topics = await service.GetAllTopicsAsync();

        topics.Select(x => x.Name).Take(2).Should().Equal("Pinned-Early", "Pinned-Late");
    }

    private static TestDbContextFactory CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"topic-tests-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static TopicService CreateService(TestDbContextFactory factory, out Mock<IClientProxy> clientProxyMock)
    {
        clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(clients => clients.All).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<ShareHub>>();
        hubContextMock.Setup(context => context.Clients).Returns(hubClientsMock.Object);

        return new TopicService(factory, hubContextMock.Object);
    }
}
