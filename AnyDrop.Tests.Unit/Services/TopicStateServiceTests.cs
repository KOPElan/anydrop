using AnyDrop.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Services;

public class TopicStateServiceTests
{
    [Fact]
    public void SetSelectedTopic_WhenValueChanges_RaisesSelectedTopicChanged()
    {
        var service = new TopicStateService();
        var raisedCount = 0;
        service.SelectedTopicChanged += () => raisedCount++;

        service.SetSelectedTopic(Guid.NewGuid());

        raisedCount.Should().Be(1);
    }

    [Fact]
    public void SetSelectedTopic_WhenValueUnchanged_DoesNotRaiseSelectedTopicChanged()
    {
        var topicId = Guid.NewGuid();
        var service = new TopicStateService();
        var raisedCount = 0;
        service.SelectedTopicChanged += () => raisedCount++;

        service.SetSelectedTopic(topicId);
        service.SetSelectedTopic(topicId);

        raisedCount.Should().Be(1);
    }

    [Fact]
    public void NotifyTopicsChanged_AlwaysRaisesTopicsChanged()
    {
        var service = new TopicStateService();
        var raisedCount = 0;
        service.TopicsChanged += () => raisedCount++;

        service.NotifyTopicsChanged();
        service.NotifyTopicsChanged();

        raisedCount.Should().Be(2);
    }
}
