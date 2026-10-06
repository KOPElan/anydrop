using AnyDrop.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// 上传文件扩展名白名单的测试。
///
/// 背景：存储名此前直接采用 <c>Path.GetExtension(fileName)</c> 的结果，而它不做任何字符校验，
/// 只返回「最后一个点之后的部分」。该值会进入缩略图生成的 ffmpeg 命令行，
/// 攻击者只要让文件名包含一个双引号即可闭合引号、注入任意 ffmpeg 参数
/// （Linux 上双引号是合法文件名字符，而 Linux 正是 Docker 部署的主要目标平台）。
/// </summary>
public class FileExtensionSanitizationTests : IDisposable
{
    private readonly string _basePath =
        Path.Combine(AppContext.BaseDirectory, "test-ext", Guid.NewGuid().ToString("N"));

    private readonly LocalFileStorageService _service;

    public FileExtensionSanitizationTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = _basePath
            })
            .Build();

        _service = new LocalFileStorageService(configuration);
    }

    [Theory]
    [InlineData("photo.png", ".png")]
    [InlineData("archive.tar", ".tar")]
    [InlineData("a.BIN", ".BIN")]
    [InlineData("clip.mp4", ".mp4")]
    [InlineData("no-extension", "")]
    [InlineData("trailing.", "")]
    public void GetSafeExtension_AllowedForms_AreKept(string fileName, string expected)
        => LocalFileStorageService.GetSafeExtension(fileName).Should().Be(expected);

    [Theory]
    [InlineData("clip.\" -y evil")]              // 引号注入：本次修复针对的真实攻击载荷
    [InlineData("evil.pn g")]                     // 空格
    [InlineData("evil.;rm -rf /")]                // shell 元字符
    [InlineData("evil.verylongextensionname")]    // 超出长度上限
    [InlineData("evil.")]                         // 只有点
    public void GetSafeExtension_MaliciousOrInvalidForms_AreDropped(string fileName)
        => LocalFileStorageService.GetSafeExtension(fileName).Should().BeEmpty();

    [Fact]
    public async Task SaveFileAsync_WithQuoteInjectionAttempt_StripsExtensionFromStoredPath()
    {
        await using var source = new MemoryStream([1, 2, 3]);

        var savedPath = await _service.SaveFileAsync(source, "clip.\" -y evil", "video/mp4");

        savedPath.Should().NotContain("\"");
        savedPath.Should().NotContain(" ");
        // 非法扩展名被丢弃，存储名只剩 GUID，因此没有扩展名
        Path.GetExtension(savedPath).Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, true);
        }
    }
}
