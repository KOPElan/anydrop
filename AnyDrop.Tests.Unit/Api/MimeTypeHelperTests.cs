using System.Text;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;

namespace AnyDrop.Tests.Unit.Api;

/// <summary>
/// S0-02（SEC-07）：SVG 内联绕过。
///
/// 两部分：(1) 实测 multipart 解析是否原样保留 Content-Type 参数——决定
/// 旧的精确匹配在真实请求下是否可被绕过；(2) 规范化 + 附件下载的回归用例。
/// </summary>
public class MimeTypeHelperTests
{
    [Theory]
    [InlineData("image/svg+xml; charset=utf-8", "image/svg+xml")]
    [InlineData("image/svg+xml ", "image/svg+xml")]
    [InlineData("IMAGE/SVG+XML; CHARSET=UTF-8", "image/svg+xml")]
    [InlineData("image/png", "image/png")]
    [InlineData(null, "application/octet-stream")]
    [InlineData("   ", "application/octet-stream")]
    [InlineData("text/html; charset=utf-8", "text/html")]
    public void Normalize_StripsParametersWhitespaceAndCase(string? input, string expected)
    {
        MimeTypeHelper.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("image/svg+xml; charset=utf-8")]
    [InlineData("image/svg+xml ")]
    [InlineData("IMAGE/SVG+XML")]
    public void IsSvg_DetectsParameterAndCaseVariants(string mimeType)
    {
        MimeTypeHelper.IsSvg(mimeType).Should().BeTrue();
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg; charset=utf-8")]
    [InlineData("application/octet-stream")]
    public void IsSvg_RejectsNonSvg(string mimeType)
    {
        MimeTypeHelper.IsSvg(mimeType).Should().BeFalse();
    }

    /// <summary>
    /// 实测：ASP.NET Core 的 MultipartReader 如何处理文件段的 Content-Type。
    /// 结论（2026-11-14 实测）：参数被原样保留（<c>; charset=utf-8</c> 仍在），
    /// 因此旧代码的 <c>mimeType.Equals("image/svg+xml")</c> 在真实上传请求下
    /// 可被 <c>image/svg+xml; charset=utf-8</c> 绕过；行尾空白则由解析器剥离。
    /// </summary>
    [Theory]
    [InlineData("image/svg+xml; charset=utf-8", "image/svg+xml; charset=utf-8")]
    [InlineData("image/svg+xml ", "image/svg+xml")]
    public async Task MultipartReader_RetainsContentTypeParameters(string contentType, string expected)
    {
        const string boundary = "----test-boundary";
        var body =
            $"------test-boundary\r\n" +
            "Content-Disposition: form-data; name=\"file\"; filename=\"evil.svg\"\r\n" +
            $"Content-Type: {contentType}\r\n" +
            "\r\n" +
            "<svg></svg>\r\n" +
            "------test-boundary--\r\n";

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var reader = new MultipartReader(boundary, stream);
        var section = await reader.ReadNextSectionAsync();

        section.Should().NotBeNull();
        // 探针断言：记录框架的实际行为。若第一组断言失败，说明框架已剥离参数，
        // 此时 S0-02 的规范化仍作为纵深保留，但绕过的前提即不存在。
        section!.ContentType.Should().Be(expected);
    }
}
