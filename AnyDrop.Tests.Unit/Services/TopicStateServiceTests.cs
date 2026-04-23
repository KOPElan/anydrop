using AnyDrop.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnyDrop.Tests.Unit.Services;

public class TopicStateServiceTests
{
    [Fact]
    public async Task SetSelectedTopicAsync_WhenValueChanges_RaisesSelectedTopicChanged()
    {
        var service = new TopicStateService(NullLogger<TopicStateService>.Instance);
        var raisedCount = 0;
        service.SelectedTopicChanged += () =>
        {
            raisedCount++;
            return Task.CompletedTask;
        };

        var topicId = Guid.NewGuid();
        await service.SetSelectedTopicAsync(topicId);

        raisedCount.Should().Be(1);
        service.SelectedTopicId.Should().Be(topicId);
    }

    [Fact]
    public async Task SetSelectedTopicAsync_WhenValueUnchanged_DoesNotRaiseSelectedTopicChanged()
    {
        var topicId = Guid.NewGuid();
        var service = new TopicStateService(NullLogger<TopicStateService>.Instance);
        var raisedCount = 0;
        service.SelectedTopicChanged += () =>
        {
            raisedCount++;
            return Task.CompletedTask;
        };

        await service.SetSelectedTopicAsync(topicId);
        await service.SetSelectedTopicAsync(topicId);

        raisedCount.Should().Be(1);
    }

    [Fact]
    public async Task SetSelectedTopicAsync_WhenSwitchingToNull_RaisesSelectedTopicChanged()
    {
        var topicId = Guid.NewGuid();
        var service = new TopicStateService(NullLogger<TopicStateService>.Instance);
        var raisedCount = 0;
        service.SelectedTopicChanged += () =>
        {
            raisedCount++;
            return Task.CompletedTask;
        };

        await service.SetSelectedTopicAsync(topicId);
        await service.SetSelectedTopicAsync(null);

        raisedCount.Should().Be(2);
        service.SelectedTopicId.Should().BeNull();
    }

    [Fact]
    public async Task SetSelectedTopicAsync_WhenNullToNull_DoesNotRaiseSelectedTopicChanged()
    {
        var service = new TopicStateService(NullLogger<TopicStateService>.Instance);
        var raisedCount = 0;
        service.SelectedTopicChanged += () =>
        {
            raisedCount++;
            return Task.CompletedTask;
        };

        await service.SetSelectedTopicAsync(null);

        raisedCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyTopicsChangedAsync_AlwaysRaisesTopicsChanged()
    {
        var service = new TopicStateService(NullLogger<TopicStateService>.Instance);
        var raisedCount = 0;
        service.TopicsChanged += () =>
        {
            raisedCount++;
            return Task.CompletedTask;
        };

        await service.NotifyTopicsChangedAsync();
        await service.NotifyTopicsChangedAsync();

        raisedCount.Should().Be(2);
    }

    [Fact]
    public async Task SetSelectedTopicAsync_WhenHandlerThrows_DoesNotThrowAndContinues()
    {
        var service = new TopicStateService(NullLogger<TopicStateService>.Instance);
        var raisedCount = 0;
        service.SelectedTopicChanged += () => Task.FromException(new InvalidOperationException("boom"));
        service.SelectedTopicChanged += () =>
        {
            raisedCount++;
            return Task.CompletedTask;
        };

        var act = async () => await service.SetSelectedTopicAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
        raisedCount.Should().Be(1);
    }

    [Fact]
    public async Task NotifyTopicsChangedAsync_WhenHandlerThrows_DoesNotThrowAndContinues()
    {
        var service = new TopicStateService(NullLogger<TopicStateService>.Instance);
        var raisedCount = 0;
        service.TopicsChanged += () => Task.FromException(new InvalidOperationException("boom"));
        service.TopicsChanged += () =>
        {
            raisedCount++;
            return Task.CompletedTask;
        };

        var act = async () => await service.NotifyTopicsChangedAsync();

        await act.Should().NotThrowAsync();
        raisedCount.Should().Be(1);
    }
}
