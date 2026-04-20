using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Components.Pages;

public partial class TopicSearch
{
    [Parameter] public Guid TopicId { get; set; }

    [Inject] public required IShareService ShareService { get; set; }
    [Inject] public required ITopicService TopicService { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required ILogger<TopicSearch> Logger { get; set; }

    // 当前激活的标签页
    private string _activeTab = "text";

    // 主题名称
    private string? _topicName;

    // ─── 文字搜索 ───
    private string _searchQuery = string.Empty;
    private List<ShareItemDto> _searchResults = [];
    private bool _searchHasMore;
    private string? _searchNextCursor;
    private bool _isSearching;
    private bool _searchPerformed;

    // ─── 日期查找 ───
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private List<ShareItemDto> _dateResults = [];
    private bool _isLoadingDate;
    private bool _dateSearchPerformed;

    // ─── 媒体/文件/链接 标签页通用 ───
    private List<ShareItemDto> _typeResults = [];
    private bool _typeHasMore;
    private string? _typeNextCursor;
    private bool _isLoadingType;

    // ─── 图片大图预览 ───
    private string? _previewImageUrl;

    protected override async Task OnInitializedAsync()
    {
        await LoadTopicNameAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        await LoadTopicNameAsync();
    }

    private async Task LoadTopicNameAsync()
    {
        try
        {
            var allTopics = await TopicService.GetAllTopicsAsync();
            var topic = allTopics.FirstOrDefault(t => t.Id == TopicId);

            if (topic is null)
            {
                var archived = await TopicService.GetArchivedTopicsAsync();
                topic = archived.FirstOrDefault(t => t.Id == TopicId);
            }

            _topicName = topic?.Name;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load topic name for {TopicId}", TopicId);
        }
    }

    // ─────────────────────────── 标签页切换 ───────────────────────────

    private async Task SwitchTabAsync(string tab)
    {
        if (_activeTab == tab)
        {
            return;
        }

        _activeTab = tab;

        // 切换到媒体/文件/链接标签时自动加载
        if (tab is "image" or "video" or "file" or "link")
        {
            await LoadTypeResultsAsync(reset: true);
        }
    }

    // ─────────────────────────── 文字搜索 ───────────────────────────

    private async Task RunSearchAsync()
    {
        var trimmed = _searchQuery.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        _isSearching = true;
        _searchPerformed = false;
        _searchResults.Clear();
        _searchHasMore = false;
        _searchNextCursor = null;

        try
        {
            var response = await ShareService.SearchTopicMessagesAsync(TopicId, trimmed, limit: 50);
            _searchResults = [.. response.Messages];
            _searchHasMore = response.HasMore;
            _searchNextCursor = response.NextCursor;
            _searchPerformed = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to search topic {TopicId} with query '{Query}'", TopicId, trimmed);
        }
        finally
        {
            _isSearching = false;
        }
    }

    private async Task LoadMoreSearchResultsAsync()
    {
        if (!_searchHasMore || string.IsNullOrWhiteSpace(_searchNextCursor) || _isSearching)
        {
            return;
        }

        _isSearching = true;
        try
        {
            var before = DateTimeOffset.TryParse(_searchNextCursor, out var dt) ? dt : (DateTimeOffset?)null;
            var response = await ShareService.SearchTopicMessagesAsync(TopicId, _searchQuery.Trim(), limit: 50, before: before);
            _searchResults.AddRange(response.Messages);
            _searchHasMore = response.HasMore;
            _searchNextCursor = response.NextCursor;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load more search results for topic {TopicId}", TopicId);
        }
        finally
        {
            _isSearching = false;
        }
    }

    private async Task HandleSearchKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (string.Equals(e.Key, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            await RunSearchAsync();
        }
    }

    // ─────────────────────────── 日期查找 ───────────────────────────

    private async Task LoadDateResultsAsync()
    {
        _isLoadingDate = true;
        _dateSearchPerformed = false;
        _dateResults.Clear();

        try
        {
            var results = await ShareService.GetTopicMessagesByDateAsync(TopicId, _selectedDate);
            _dateResults = [.. results];
            _dateSearchPerformed = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load messages by date for topic {TopicId}", TopicId);
        }
        finally
        {
            _isLoadingDate = false;
        }
    }

    private async Task HandleDateChanged(ChangeEventArgs e)
    {
        if (DateOnly.TryParse(e.Value?.ToString(), out var date))
        {
            _selectedDate = date;
            await LoadDateResultsAsync();
        }
    }

    // ─────────────────────────── 媒体/文件/链接 ───────────────────────────

    private ShareContentType GetActiveContentType()
    {
        return _activeTab switch
        {
            "image" => ShareContentType.Image,
            "video" => ShareContentType.Video,
            "file"  => ShareContentType.File,
            "link"  => ShareContentType.Link,
            _ => ShareContentType.Text
        };
    }

    private async Task LoadTypeResultsAsync(bool reset = false)
    {
        if (reset)
        {
            _typeResults.Clear();
            _typeHasMore = false;
            _typeNextCursor = null;
        }

        _isLoadingType = true;
        try
        {
            var before = reset ? null :
                (DateTimeOffset.TryParse(_typeNextCursor, out var dt) ? dt : (DateTimeOffset?)null);

            var response = await ShareService.GetTopicMessagesByTypeAsync(
                TopicId, GetActiveContentType(), limit: 50, before: before);

            _typeResults.AddRange(response.Messages);
            _typeHasMore = response.HasMore;
            _typeNextCursor = response.NextCursor;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load type results for topic {TopicId}, tab {Tab}", TopicId, _activeTab);
        }
        finally
        {
            _isLoadingType = false;
        }
    }

    // ─────────────────────────── 导航到聊天 ───────────────────────────

    /// <summary>导航回聊天页，并高亮定位到指定消息。</summary>
    private void NavigateToMessage(Guid messageId)
    {
        NavigationManager.NavigateTo($"/?highlight={messageId}");
    }

    // ─────────────────────────── 图片预览 ───────────────────────────

    private void OpenImagePreview(string url)
    {
        _previewImageUrl = url;
    }

    private void CloseImagePreview()
    {
        _previewImageUrl = null;
    }

    // ─────────────────────────── 工具方法 ───────────────────────────

    private static string GetFileUrl(Guid itemId, bool download = false)
        => download
            ? $"/api/v1/share-items/{itemId}/file?download=true"
            : $"/api/v1/share-items/{itemId}/file";

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }

    private static string FormatMessageTime(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        return local.ToString("yyyy/MM/dd HH:mm");
    }
}
