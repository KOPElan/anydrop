namespace AnyDrop.Services;

public interface ITopicStateService
{
    Guid? SelectedTopicId { get; }

    event Action? SelectedTopicChanged;

    event Action? TopicsChanged;

    void SetSelectedTopic(Guid? topicId);

    void NotifyTopicsChanged();
}
