using System.Text;
using AnyDrop.Api;
using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AnyDrop.Tests.Unit.Api;

/// <summary>
/// 下载响应策略的测试。
///
/// 判断依据是 <c>FileDownloadName</c>：有值即代表响应带
/// <c>Content-Disposition: attachment</c>（强制下载），为空则是内联展示。
///
/// 此前用黑名单判定（只拦 text/html、image/svg+xml、javascript），
/// 会漏掉 <c>application/xhtml+xml</c> 这类同样能执行脚本的类型。
/// </summary>
public class ShareItemEndpointsDownloadTests : IDisposable
{
    private readonly string _basePath =
        Path.Combine(AppContext.BaseDirectory, "test-download", Guid.NewGuid().ToString("N"));

    private readonly LocalFileStorageService _storage;

    public ShareItemEndpointsDownloadTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = _basePath
            })
            .Build();

        _storage = new LocalFileStorageService(configuration, TimeProvider.System);
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-download-{Guid.NewGuid():N}")
            .Options;

        return new AnyDropDbContext(options);
    }

    private async Task<Guid> SeedItemAsync(AnyDropDbContext db, string mimeType, string fileName)
    {
        var stored = await _storage.SaveFileAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("<html>payload</html>")), fileName, mimeType);

        var contentType = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? ShareContentType.Image
            : mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                ? ShareContentType.Video
                : ShareContentType.File;

        var item = new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = contentType,
            Content = stored,
            FileName = fileName,
            MimeType = mimeType,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ShareItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<string?> GetDownloadNameAsync(AnyDropDbContext db, Guid id, bool? download)
    {
        var result = await ShareItemEndpoints.GetFileAsync(
            id, download, db, _storage, CancellationToken.None);

        var fileResult = result.Result as FileStreamHttpResult;
        fileResult.Should().NotBeNull("文件存在时应返回文件流结果");

        try
        {
            return fileResult!.FileDownloadName;
        }
        finally
        {
            await fileResult!.FileStream.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("text/html", "evil.html")]
    [InlineData("image/svg+xml", "evil.svg")]              // SVG 可内嵌脚本
    [InlineData("application/xhtml+xml", "evil.xhtml")]    // 旧黑名单漏掉的类型
    [InlineData("application/javascript", "evil.js")]
    [InlineData("text/javascript", "evil2.js")]
    [InlineData("application/octet-stream", "unknown.bin")]
    [InlineData("application/pdf", "doc.pdf")]
    public async Task GetFileAsync_DangerousOrUnknownTypes_AreForcedToAttachment(string mimeType, string fileName)
    {
        await using var db = CreateDbContext();
        var id = await SeedItemAsync(db, mimeType, fileName);

        var downloadName = await GetDownloadNameAsync(db, id, download: null);

        downloadName.Should().Be(fileName, "危险或未知类型必须带下载文件名，即强制作为附件");
    }

    [Theory]
    [InlineData("image/png", "a.png")]
    [InlineData("image/jpeg", "a.jpg")]
    [InlineData("video/mp4", "a.mp4")]
    [InlineData("audio/mpeg", "a.mp3")]
    public async Task GetFileAsync_SafeMediaTypes_AreServedInline(string mimeType, string fileName)
    {
        await using var db = CreateDbContext();
        var id = await SeedItemAsync(db, mimeType, fileName);

        (await GetDownloadNameAsync(db, id, download: null)).Should().BeNull();
    }

    [Fact]
    public async Task GetFileAsync_WithDownloadFlag_ForcesAttachmentEvenForImages()
    {
        await using var db = CreateDbContext();
        var id = await SeedItemAsync(db, "image/png", "a.png");

        (await GetDownloadNameAsync(db, id, download: true)).Should().Be("a.png");
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }
}
