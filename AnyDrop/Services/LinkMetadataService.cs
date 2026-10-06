using System.Net;
using System.Net.Sockets;
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
    /// <summary>
    /// 本服务使用的命名 HttpClient。Program.cs 用它注册禁用自动重定向、
    /// 并在建连前校验目标 IP 的处理器。
    /// </summary>
    public const string HttpClientName = "link-metadata";

    /// <summary>允许跟随的最大重定向跳数，每一跳都会重新做安全校验。</summary>
    private const int MaxRedirects = 3;

    /// <summary>
    /// 创建用于抓取外链的处理器。
    ///
    /// 两个关键设置：
    ///   * <c>AllowAutoRedirect = false</c>：自动重定向会让校验只作用于初始 URL。
    ///     一个公网地址只要 302 到 <c>169.254.169.254</c> 之类的内网目标即可绕过防护。
    ///   * <c>ConnectCallback</c>：在真正建连前校验解析出的 IP，
    ///     这消除了「校验时是公网 IP、建连时解析成内网 IP」的 DNS 重绑定时间窗。
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            if (addresses.Length == 0)
            {
                throw new HttpRequestException($"无法解析主机：{context.DnsEndPoint.Host}");
            }

            foreach (var address in addresses)
            {
                if (!IsPublicAddress(address))
                {
                    throw new HttpRequestException(
                        $"拒绝连接到非公网地址 {address}（主机 {context.DnsEndPoint.Host}）。");
                }
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

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

            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; AnyDropBot/1.0; +https://github.com/KOPElan/anydrop)");

            var currentUrl = url;

            // 手动跟随重定向，每一跳都重新校验目标地址。
            // 直接把重定向交给 HttpClient 处理，防护就只覆盖最初的 URL。
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!await IsUrlSafeAsync(currentUrl, cts.Token))
                {
                    logger.LogWarning("跳过外链元数据抓取，目标地址不被允许：{Url}", currentUrl);
                    return (null, null);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return (null, null);
                    }

                    currentUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(currentUrl), location).ToString();
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("抓取外链元数据时收到非成功状态 {Status}：{Url}",
                        (int)response.StatusCode, currentUrl);
                    return (null, null);
                }

                return await ReadMetadataAsync(response, cts.Token);
            }

            logger.LogWarning("外链重定向超过 {Max} 跳，放弃抓取：{Url}", MaxRedirects, url);
            return (null, null);
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

    private static bool IsRedirect(HttpStatusCode status)
        => status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static async Task<(string? Title, string? Description)> ReadMetadataAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var buffer = new char[MaxHtmlReadBytes];
        var read = await reader.ReadBlockAsync(buffer, ct);
        var html = new string(buffer, 0, read);

        var title = ExtractFirst(html, OgTitlePattern, OgTitlePatternAlt)
                    ?? ExtractFirst(html, TitleTagPattern);

        var description = ExtractFirst(html, OgDescPattern, OgDescPatternAlt)
                          ?? ExtractFirst(html, MetaDescPattern, MetaDescPatternAlt);

        return (Decode(title), Decode(description));
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

    /// <summary>
    /// SSRF 防护：验证 URL 是否指向公网 http/https 资源。
    ///
    /// 除了检查字面量主机，还会解析 DNS 并要求**所有**解析结果都是公网地址。
    /// 只检查主机名字符串时，一个 A 记录指向 192.168.x.x 的域名即可绕过防护。
    /// </summary>
    internal static async Task<bool> IsUrlSafeAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // 用 DnsSafeHost 而不是 Host：后者对 IPv6 字面量会返回带方括号的形式（如 "[::1]"），
        // IPAddress.TryParse 无法解析，于是公网 IPv6 地址会被误判成主机名而遭到拒绝。
        var host = uri.DnsSafeHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // 常见内网主机名直接拒绝，省一次 DNS 查询
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return IsPublicAddress(literal);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException)
        {
            return false;
        }

        // 要求全部解析结果都是公网地址：只要有一个内网地址就不放行
        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    /// <summary>
    /// 判断 IP 是否为公网地址（排除 loopback/私有/链路本地/云元数据/多播/保留段）。
    /// </summary>
    internal static bool IsPublicAddress(IPAddress ip)
    {
        // ::ffff:127.0.0.1 这类 IPv4-mapped 地址必须先归一化，
        // 否则会跳过下面的 IPv4 判断而被误判为公网。
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            {
                return false;
            }

            if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None))
            {
                return false;
            }

            // fc00::/7 — unique local address（IsIPv6SiteLocal 只覆盖已废弃的 fec0::/10）
            var v6 = ip.GetAddressBytes();
            if ((v6[0] & 0xFE) == 0xFC)
            {
                return false;
            }

            return true;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        return bytes switch
        {
            [0, ..] => false,                                        // 0.0.0.0/8
            [10, ..] => false,                                       // 10.0.0.0/8
            [100, var b, ..] when b is >= 64 and <= 127 => false,    // 100.64.0.0/10（CGNAT）
            [127, ..] => false,                                      // 127.0.0.0/8
            [169, 254, ..] => false,                                 // 169.254.0.0/16（云元数据）
            [172, var b, ..] when b is >= 16 and <= 31 => false,     // 172.16.0.0/12
            [192, 0, 0, ..] => false,                                // 192.0.0.0/24
            [192, 168, ..] => false,                                 // 192.168.0.0/16
            [198, 18 or 19, ..] => false,                            // 198.18.0.0/15（基准测试）
            // 注意这里必须用关系模式：写成 [240, ..] 只匹配首字节恰好为 240 的地址，
            // 255.255.255.255 会漏过去被误判为公网。
            [>= 224, ..] => false,                                   // 224.0.0.0/4 多播 + 240.0.0.0/4 保留
            _ => true
        };
    }
}
