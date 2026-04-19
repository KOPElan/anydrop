using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using AnyDrop.Services;
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
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _);

        var act = () => service.CreateTopicAsync(new CreateTopicRequest(" "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateTopicAsync_WhenNameIsValid_ReturnsTopicDto()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _);

        var result = await service.CreateTopicAsync(new CreateTopicRequest("工作"));

        result.Name.Should().Be("工作");
        result.SortOrder.Should().Be(int.MaxValue);
        (await dbContext.Topics.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetAllTopicsAsync_ReturnsTopicsInCorrectSortOrder()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.Topics.AddRange(
            new Topic { Id = Guid.NewGuid(), Name = "C", SortOrder = 1, CreatedAt = now.AddMinutes(-5), LastMessageAt = now.AddMinutes(-2) },
            new Topic { Id = Guid.NewGuid(), Name = "A", SortOrder = 0, CreatedAt = now.AddMinutes(-10), LastMessageAt = now.AddMinutes(-1) },
            new Topic { Id = Guid.NewGuid(), Name = "B", SortOrder = 1, CreatedAt = now.AddMinutes(-1), LastMessageAt = null });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out _);
        var result = await service.GetAllTopicsAsync();

        result.Select(x => x.Name).Should().Equal("A", "C", "B");
    }

    [Fact]
    public async Task ReorderTopicsAsync_UpdatesSortOrderInDatabase()
    {
        await using var dbContext = CreateDbContext();
        var topicA = new Topic { Name = "A", SortOrder = 0 };
        var topicB = new Topic { Name = "B", SortOrder = 1 };
        dbContext.Topics.AddRange(topicA, topicB);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, out _);

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
        await using var dbContext = CreateDbContext();
        var topic = new Topic { Name = "A" };
        dbContext.Topics.Add(topic);
        var now = DateTimeOffset.UtcNow;
        dbContext.ShareItems.AddRange(
            new ShareItem { Content = "m1", TopicId = topic.Id, CreatedAt = now.AddMinutes(-3), ContentType = ShareContentType.Text },
            new ShareItem { Content = "m2", TopicId = topic.Id, CreatedAt = now.AddMinutes(-2), ContentType = ShareContentType.Text },
            new ShareItem { Content = "m3", TopicId = topic.Id, CreatedAt = now.AddMinutes(-1), ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out _);
        var first = await service.GetTopicMessagesAsync(topic.Id, 2, null);
        var second = await service.GetTopicMessagesAsync(topic.Id, 2, DateTimeOffset.Parse(first!.NextCursor!));

        first!.Messages.Select(x => x.Content).Should().Equal("m3", "m2");
        first.HasMore.Should().BeTrue();
        second!.Messages.Select(x => x.Content).Should().Equal("m1");
    }

    [Fact]
    public async Task DeleteTopicAsync_SetsTopicIdNullOnMessages()
    {
        await using var dbContext = CreateDbContext();
        var topic = new Topic { Name = "A" };
        dbContext.Topics.Add(topic);
        dbContext.ShareItems.Add(new ShareItem
        {
            Content = "hello",
            ContentType = ShareContentType.Text,
            TopicId = topic.Id
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out _);
        var deleted = await service.DeleteTopicAsync(topic.Id);

        deleted.Should().BeTrue();
        (await dbContext.Topics.CountAsync()).Should().Be(0);
        (await dbContext.ShareItems.SingleAsync()).TopicId.Should().BeNull();
    }

    [Fact]
    public async Task TopicMutations_BroadcastTopicsUpdated()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out var clientProxyMock);

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

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"topic-tests-{Guid.NewGuid():N}")
            .Options;
        return new AnyDropDbContext(options);
    }

    private static TopicService CreateService(AnyDropDbContext dbContext, out Mock<IClientProxy> clientProxyMock)
    {
        clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(clients => clients.All).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<ShareHub>>();
        hubContextMock.Setup(context => context.Clients).Returns(hubClientsMock.Object);

        return new TopicService(dbContext, hubContextMock.Object);
    }
}
