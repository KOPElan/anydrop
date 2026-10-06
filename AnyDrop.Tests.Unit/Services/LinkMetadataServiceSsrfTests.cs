using System.Net;
using AnyDrop.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// SSRF 防护的测试。
///
/// 这些用例覆盖此前存在的两个绕过路径：
///   * 只校验字面量主机名，A 记录指向内网的域名可以通过（现改为解析 DNS 并校验全部结果）；
///   * 跟随自动重定向，公网地址 302 到内网即可绕过（现已禁用并逐跳校验）。
/// 另外覆盖 IPv4-mapped IPv6 与 fc00::/7 这两个此前被漏掉的地址段。
/// </summary>
public class LinkMetadataServiceSsrfTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]   // 云元数据端点
    [InlineData("100.64.0.1")]        // CGNAT
    [InlineData("192.0.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]         // 多播
    [InlineData("255.255.255.255")]
    public void IsPublicAddress_PrivateAndReservedIPv4_IsFalse(string address)
        => LinkMetadataService.IsPublicAddress(IPAddress.Parse(address)).Should().BeFalse();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    public void IsPublicAddress_PublicIPv4_IsTrue(string address)
        => LinkMetadataService.IsPublicAddress(IPAddress.Parse(address)).Should().BeTrue();

    [Theory]
    [InlineData("::1")]                       // loopback
    [InlineData("fe80::1")]                   // 链路本地
    [InlineData("fc00::1")]                   // unique local（此前被漏掉）
    [InlineData("fd12:3456::1")]              // unique local
    [InlineData("::ffff:127.0.0.1")]          // IPv4-mapped loopback（此前被漏掉）
    [InlineData("::ffff:169.254.169.254")]    // IPv4-mapped 云元数据（此前被漏掉）
    public void IsPublicAddress_PrivateIPv6_IsFalse(string address)
        => LinkMetadataService.IsPublicAddress(IPAddress.Parse(address)).Should().BeFalse();

    [Theory]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    public void IsPublicAddress_PublicIPv6_IsTrue(string address)
        => LinkMetadataService.IsPublicAddress(IPAddress.Parse(address)).Should().BeTrue();

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/admin")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://localhost/")]
    [InlineData("http://foo.local/")]
    [InlineData("http://svc.internal/")]
    [InlineData("ftp://8.8.8.8/")]            // 非 http/https
    [InlineData("file:///etc/passwd")]
    public async Task IsUrlSafeAsync_DisallowedTargets_AreFalse(string url)
        => (await LinkMetadataService.IsUrlSafeAsync(url, CancellationToken.None)).Should().BeFalse();

    [Theory]
    [InlineData("https://8.8.8.8/")]
    [InlineData("http://1.1.1.1/")]
    [InlineData("https://[2606:4700:4700::1111]/")]   // 公网 IPv6 字面量不应被误拒
    public async Task IsUrlSafeAsync_PublicLiteralAddresses_AreTrue(string url)
        => (await LinkMetadataService.IsUrlSafeAsync(url, CancellationToken.None)).Should().BeTrue();

    [Fact]
    public async Task IsUrlSafeAsync_RelativeOrMalformedUrl_IsFalse()
    {
        (await LinkMetadataService.IsUrlSafeAsync("not-a-url", CancellationToken.None)).Should().BeFalse();
        (await LinkMetadataService.IsUrlSafeAsync("/relative/path", CancellationToken.None)).Should().BeFalse();
        (await LinkMetadataService.IsUrlSafeAsync(string.Empty, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public void CreateHandler_DisablesAutoRedirect()
    {
        // 自动重定向会让校验只覆盖初始 URL，是 SSRF 的主要绕过方式，必须保持关闭
        using var handler = LinkMetadataService.CreateHandler();

        handler.AllowAutoRedirect.Should().BeFalse();
        handler.ConnectCallback.Should().NotBeNull();
    }
}
