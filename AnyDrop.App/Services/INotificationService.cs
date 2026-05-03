namespace AnyDrop.App.Services;

/// <summary>本地推送通知接口。</summary>
public interface INotificationService
{
    Task<bool> RequestPermissionAsync();
    Task ShowMessageNotificationAsync(string topicName, string preview, Guid topicId);
    Task CancelAllAsync();
}
