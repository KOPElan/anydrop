namespace AnyDrop.Models;

using System.Net;

/// <summary>
/// 反向代理支持配置（对应配置节 <c>ReverseProxy</c>）。
///
/// 默认关闭。部署在 Nginx / Caddy / Traefik / Cloudflare 等代理之后时应启用，
/// 并**必须**把代理的地址或网段加入白名单。
///
/// 为什么不能省略白名单：ASP.NET Core 默认只信任 loopback，
/// 未列入可信来源时 <c>X-Forwarded-*</c> 会被直接忽略——这是安全默认值。
///
/// 为什么也不能「信任全部来源」：那等于允许任何客户端伪造 <c>X-Forwarded-For</c>，
/// 从而绕开按客户端 IP 计算的登录限流。真正的问题在于不启用时所有请求都来自代理 IP，
/// 一个人的失败尝试会把所有人（包括站主本人）一起锁住。
/// </summary>
public sealed class ReverseProxyOptions
{
    public bool Enabled { get; set; }

    /// <summary>受信任代理的 IP，例如 <c>172.18.0.2</c>。</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>受信任代理的网段（CIDR），例如 Docker 默认的 <c>172.16.0.0/12</c>。</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// 解析受信任来源。无法解析的条目会被忽略而不是让服务启动失败——
    /// 配置写错时应该退化为「不信任任何来源」（安全默认），而不是让实例起不来。
    /// </summary>
    internal (List<IPAddress> Proxies, List<IPNetwork> Networks) ParseTrustedSources()
    {
        var proxies = new List<IPAddress>();
        foreach (var proxy in KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                proxies.Add(address);
            }
        }

        var networks = new List<IPNetwork>();
        foreach (var network in KnownNetworks)
        {
            if (IPNetwork.TryParse(network, out var parsed))
            {
                networks.Add(parsed);
            }
        }

        return (proxies, networks);
    }
}
