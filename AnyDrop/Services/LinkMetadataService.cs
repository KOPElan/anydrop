using System.Net;
using System.Text.RegularExpressions;

namespace AnyDrop.Services;

/// <summary>
/// 从 HTTP/HTTPS 链接抓取 Open Graph 或 HTML meta 标签中的标题和描述。
/// 当抓取失败时静默返回 null，确保主流程不受影响。
/// </summary>
public sealed class LinkMetadataService(
    IHttpClientFactory httpClientFactory,
    ILogger<LinkMetadataService> logger)
{
    // 最多读取 64 KB，通常已足够包含所有 meta 标签，同时避免大页面造成的性能问题
    private const int MaxHtmlReadBytes = 65_536;

    // 正则超时设为 500ms，避免 ReDoS 或恶意 HTML 导致累计超时
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    private static readonly Regex OgTitlePattern =
        new(@"<meta[^>]+property\s*=\s*[""']og:title[""'][^>]+content\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex OgTitlePatternAlt =
        new(@"<meta[^>]+content\s*=\s*[""']([^""']*)[""'][^>]+property\s*=\s*[""']og:title[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex OgDescPattern =
        new(@"<meta[^>]+property\s*=\s*[""']og:description[""'][^>]+content\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex OgDescPatternAlt =
        new(@"<meta[^>]+content\s*=\s*[""']([^""']*)[""'][^>]+property\s*=\s*[""']og:description[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex MetaDescPattern =
        new(@"<meta[^>]+name\s*=\s*[""']description[""'][^>]+content\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex MetaDescPatternAlt =
        new(@"<meta[^>]+content\s*=\s*[""']([^""']*)[""'][^>]+name\s*=\s*[""']description[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex TitleTagPattern =
        new(@"<title[^>]*>([^<]+)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// 尝试抓取链接的标题和描述。
    /// 失败时返回 (null, null)，不抛出异常。
    /// </summary>
    public async Task<(string? Title, string? Description)> FetchAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; AnyDropBot/1.0; +https://github.com/KOPElan/anydrop)");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Received non-success status {Status} when fetching metadata for {Url}.",
                    (int)response.StatusCode, url);
                return (null, null);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return (null, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);

            var buffer = new char[MaxHtmlReadBytes];
            var read = await reader.ReadBlockAsync(buffer, cts.Token);
            var html = new string(buffer, 0, read);

            var title = ExtractFirst(html, OgTitlePattern, OgTitlePatternAlt)
                        ?? ExtractFirst(html, TitleTagPattern);

            var description = ExtractFirst(html, OgDescPattern, OgDescPatternAlt)
                              ?? ExtractFirst(html, MetaDescPattern, MetaDescPatternAlt);

            return (Decode(title), Decode(description));
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Metadata fetch timed out for {Url}.", url);
            return (null, null);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "HTTP error while fetching link metadata for {Url}.", url);
            return (null, null);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unexpected error while fetching link metadata for {Url}.", url);
            return (null, null);
        }
    }

    private static string? ExtractFirst(string html, params Regex[] patterns)
    {
        foreach (var pattern in patterns)
        {
            try
            {
                var m = pattern.Match(html);
                if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                {
                    return m.Groups[1].Value.Trim();
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // 超时时跳过该模式，继续尝试下一个
            }
        }

        return null;
    }

    /// <summary>将 HTML 实体解码为可显示的文本（如 &amp; → &）。</summary>
    private static string? Decode(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : WebUtility.HtmlDecode(value);
}
