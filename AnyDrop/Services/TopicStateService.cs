namespace AnyDrop.Services;

public sealed class TopicStateService : ITopicStateService
{
    private Guid? selectedTopicId;

    public Guid? SelectedTopicId => selectedTopicId;

    public event Action? SelectedTopicChanged;

    public event Action? TopicsChanged;

    public void SetSelectedTopic(Guid? topicId)
    {
        if (selectedTopicId == topicId)
        {
            return;
        }

        selectedTopicId = topicId;
        SelectedTopicChanged?.Invoke();
    }

    public void NotifyTopicsChanged()
    {
        TopicsChanged?.Invoke();
    }
}
