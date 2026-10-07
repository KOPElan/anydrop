using System.Net;
using AnyDrop.Models;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Models;

/// <summary>
/// 反向代理可信来源解析的测试。
///
/// 这里的关键安全属性是「宁可少信任」：配置写错时不能退化成信任所有来源，
/// 否则任何客户端都能伪造 X-Forwarded-For 绕开按 IP 的登录限流。
/// </summary>
public class ReverseProxyOptionsTests
{
    [Fact]
    public void ParseTrustedSources_WithValidEntries_ParsesBothLists()
    {
        var options = new ReverseProxyOptions
        {
            KnownProxies = ["172.18.0.2", "2001:db8::1"],
            KnownNetworks = ["172.16.0.0/12", "10.0.0.0/8"]
        };

        var (proxies, networks) = options.ParseTrustedSources();

        proxies.Should().HaveCount(2);
        proxies.Should().Contain(IPAddress.Parse("172.18.0.2"));
        proxies.Should().Contain(IPAddress.Parse("2001:db8::1"));

        networks.Should().HaveCount(2);
        networks[0].Contains(IPAddress.Parse("172.20.0.1")).Should().BeTrue();
        networks[1].Contains(IPAddress.Parse("10.1.2.3")).Should().BeTrue();
    }

    [Fact]
    public void ParseTrustedSources_SkipsInvalidEntriesInsteadOfThrowing()
    {
        // 配置写错不应让实例起不来，而应退化为不信任该条目
        var options = new ReverseProxyOptions
        {
            KnownProxies = ["not-an-ip", "172.18.0.2", ""],
            KnownNetworks = ["999.999.999.999/24", "10.0.0.0/8", "garbage"]
        };

        var (proxies, networks) = options.ParseTrustedSources();

        proxies.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("172.18.0.2"));
        networks.Should().ContainSingle();
    }

    [Fact]
    public void ParseTrustedSources_WhenEmpty_TrustsNothing()
    {
        var (proxies, networks) = new ReverseProxyOptions().ParseTrustedSources();

        proxies.Should().BeEmpty("默认必须不信任任何来源，X-Forwarded-* 才会被忽略");
        networks.Should().BeEmpty();
    }

    [Fact]
    public void DefaultOptions_AreDisabled()
    {
        // 默认关闭：未显式配置时不应接受任何转发的头部
        new ReverseProxyOptions().Enabled.Should().BeFalse();
    }
}
