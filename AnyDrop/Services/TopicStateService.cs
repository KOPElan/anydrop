namespace AnyDrop.Services;

public sealed class TopicStateService : ITopicStateService
{
    private Guid? _selectedTopicId;

    public Guid? SelectedTopicId => _selectedTopicId;

    public event Func<Task>? SelectedTopicChanged;

    public event Func<Task>? TopicsChanged;

    public async Task SetSelectedTopicAsync(Guid? topicId)
    {
        if (_selectedTopicId == topicId)
        {
            return;
        }

        _selectedTopicId = topicId;
        await InvokeHandlersAsync(SelectedTopicChanged);
    }

    public Task NotifyTopicsChangedAsync()
    {
        return InvokeHandlersAsync(TopicsChanged);
    }

    private static Task InvokeHandlersAsync(Func<Task>? handlers)
    {
        if (handlers is null)
        {
            return Task.CompletedTask;
        }

        var invocationList = handlers.GetInvocationList()
            .Cast<Func<Task>>();
        return Task.WhenAll(invocationList.Select(handler => handler()));
    }
}
