using Microsoft.Extensions.Logging;

namespace AnyDrop.Services;

public sealed class TopicStateService(ILogger<TopicStateService> logger) : ITopicStateService
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
        await InvokeHandlersAsync(SelectedTopicChanged, nameof(SelectedTopicChanged));
    }

    public Task NotifyTopicsChangedAsync()
    {
        return InvokeHandlersAsync(TopicsChanged, nameof(TopicsChanged));
    }

    private async Task InvokeHandlersAsync(Func<Task>? multicastHandler, string eventName)
    {
        if (multicastHandler is null)
        {
            return;
        }

        var invocationList = multicastHandler.GetInvocationList()
            .Cast<Func<Task>>();
        foreach (var handler in invocationList)
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Topic state handler {HandlerName} failed for event {EventName}. It was skipped and other handlers continued.",
                    handler.Method.Name,
                    eventName);
            }
        }
    }
}
