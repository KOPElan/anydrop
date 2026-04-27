using AnyDrop.Models;
using AnyDrop.Resources;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using System.Globalization;

namespace AnyDrop.Components.Pages;

public partial class TopicSearch
{
    [Parameter] public Guid TopicId { get; set; }

    [Inject] public required IShareService ShareService { get; set; }
    [Inject] public required ITopicService TopicService { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required ILogger<TopicSearch> Logger { get; set; }
    [Inject] public required IJSRuntime JS { get; set; }
    [Inject] public required IStringLocalizer<SharedStrings> L { get; set; }

    // 当前激活的标签页
    private string _activeTab = "text";

    // 主题名称
    private string? _topicName;

    // 用于时间显示的浏览器时区（IANA ID）
    private string _browserTimeZoneId = "UTC";
    private TimeZoneInfo _displayTimeZone = TimeZoneInfo.Local;

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

    // 日历条：当前 7 天窗口的起始日期（周一对齐，不超过今天）
    private DateOnly _calendarWindowStart;
    // 有消息记录的日期集合（当前窗口内）
    private IReadOnlyCollection<DateOnly> _activeDates = [];
    private bool _isLoadingActiveDates;

    // ─── 媒体/文件/链接 标签页通用 ───
    private List<ShareItemDto> _typeResults = [];
    private bool _typeHasMore;
    private string? _typeNextCursor;
    private bool _isLoadingType;

    // ─── 图片大图预览 ───
    private string? _previewImageUrl;

    protected override async Task OnInitializedAsync()
    {
        _calendarWindowStart = CalcDefaultWindowStart();
        await LoadTopicNameAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            _browserTimeZoneId = await JS.InvokeAsync<string>("AnyDropInterop.getBrowserTimeZone");
            try { _displayTimeZone = TimeZoneInfo.FindSystemTimeZoneById(_browserTimeZoneId); }
            catch { _displayTimeZone = TimeZoneInfo.Local; }
        }
        catch { /* JS interop failure - keep UTC */ }
        await InvokeAsync(StateHasChanged);
    }

    protected override async Task OnParametersSetAsync()
    {
        await LoadTopicNameAsync();
    }

    private async Task LoadTopicNameAsync()
    {
        try
        {
            var topic = await TopicService.GetTopicByIdAsync(TopicId);
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

        if (tab == "date")
        {
            await LoadCalendarActiveDatesAsync();
            // 默认加载今天的消息（_selectedDate 已初始化为今天）
            await LoadDateResultsAsync();
        }
        else if (tab is "image" or "video" or "file" or "link")
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

    private async Task HandleSearchKeyDown(KeyboardEventArgs e)
    {
        if (string.Equals(e.Key, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            await RunSearchAsync();
        }
    }

    // ─────────────────────────── 日期查找 ───────────────────────────

    /// <summary>计算默认日历窗口起始（今天往前推 6 天，确保今天可见）。</summary>
    private static DateOnly CalcDefaultWindowStart()
    {
        return DateOnly.FromDateTime(DateTime.Today).AddDays(-6);
    }

    /// <summary>日历条窗口中的 7 天列表（升序）。</summary>
    private IEnumerable<DateOnly> CalendarDays
        => Enumerable.Range(0, 7).Select(i => _calendarWindowStart.AddDays(i));

    /// <summary>今天日期，用于禁止导航到未来。</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private bool CanGoForward => _calendarWindowStart.AddDays(6) < Today;

    private async Task ShiftCalendarAsync(int days)
    {
        var candidate = _calendarWindowStart.AddDays(days);
        // 不允许窗口末尾超过今天
        if (days > 0 && candidate.AddDays(6) > Today)
        {
            candidate = Today.AddDays(-6);
        }

        _calendarWindowStart = candidate;
        await LoadCalendarActiveDatesAsync();
    }

    private async Task LoadCalendarActiveDatesAsync()
    {
        _isLoadingActiveDates = true;
        try
        {
            var end = _calendarWindowStart.AddDays(6);
            _activeDates = await ShareService.GetTopicActiveDatesAsync(TopicId, _calendarWindowStart, end);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load active dates for topic {TopicId}", TopicId);
            _activeDates = [];
        }
        finally
        {
            _isLoadingActiveDates = false;
        }
    }

    private async Task SelectCalendarDateAsync(DateOnly date)
    {
        if (date > Today) return;
        _selectedDate = date;
        await LoadDateResultsAsync();
    }

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

    private async Task HandleDatePickerChanged(ChangeEventArgs e)
    {
        if (DateOnly.TryParse(e.Value?.ToString(), out var date) && date <= Today)
        {
            _selectedDate = date;
            // 将日历窗口移到包含所选日期的位置：以 date 为末端，往前取 6 天
            _calendarWindowStart = date.AddDays(-6);
            // 若窗口末端超过今天，则以今天为末端（始终显示今天）
            if (_calendarWindowStart.AddDays(6) > Today)
            {
                _calendarWindowStart = Today.AddDays(-6);
            }

            await LoadCalendarActiveDatesAsync();
            await LoadDateResultsAsync();
        }
    }

    /// <summary>触发隐藏的 date input 弹出系统日历选择器。</summary>
    private async Task OpenDatePickerAsync()
    {
        await JS.InvokeVoidAsync("AnyDropInterop.showDatePicker", "datepicker-hidden");
    }

    // ─────────────────────────── 媒体/文件/链接 ───────────────────────────

    private ShareContentType GetActiveContentType()
    {
        return _activeTab switch
        {
            "image" => ShareContentType.Image,
            "video" => ShareContentType.Video,
            "file" => ShareContentType.File,
            "link" => ShareContentType.Link,
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

    private IEnumerable<IGrouping<DateOnly, ShareItemDto>> GroupByDate(List<ShareItemDto> items)
        => items
            .GroupBy(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(m.CreatedAt.UtcDateTime, _displayTimeZone)))
            .OrderByDescending(g => g.Key);

    private string FormatGroupDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (date == today) return L["Search_Today"];
        if (date == today.AddDays(-1)) return L["Search_Yesterday"];
        return FormatDateLabel(date);
    }

    private static string FormatDateLabel(DateOnly date)
    {
        return date.ToString("d", CultureInfo.CurrentCulture);
    }

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

    private string FormatMessageTime(DateTimeOffset time)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(time.UtcDateTime, _displayTimeZone);
        return local.ToString("yyyy/MM/dd HH:mm");
    }
}
