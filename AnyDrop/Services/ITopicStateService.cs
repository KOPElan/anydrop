namespace AnyDrop.Services;

public interface ITopicStateService
{
    Guid? SelectedTopicId { get; }

    event Func<Task>? SelectedTopicChanged;

    event Func<Task>? TopicsChanged;

    Task SetSelectedTopicAsync(Guid? topicId);

    Task NotifyTopicsChangedAsync();
}
