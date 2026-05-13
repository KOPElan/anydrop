using AnyDrop.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AnyDrop.Tests.Unit.Services;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _basePath = Path.Combine(AppContext.BaseDirectory, "test-storage", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorageService _service;

    public LocalFileStorageServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = _basePath
            })
            .Build();

        _service = new LocalFileStorageService(configuration);
    }

    [Fact]
    public async Task SaveAndGetFileAsync_ShouldRoundTrip()
    {
        await using var source = new MemoryStream([1, 2, 3, 4]);
        var savedPath = await _service.SaveFileAsync(source, "demo.bin", "application/octet-stream");

        await using var readStream = await _service.GetFileAsync(savedPath);
        using var reader = new MemoryStream();
        await readStream.CopyToAsync(reader);

        reader.ToArray().Should().Equal([1, 2, 3, 4]);
    }

    [Fact]
    public async Task DeleteFileAsync_ShouldRemoveFile()
    {
        await using var source = new MemoryStream([8, 9]);
        var savedPath = await _service.SaveFileAsync(source, "demo.bin", "application/octet-stream");

        await _service.DeleteFileAsync(savedPath);

        var act = () => _service.GetFileAsync(savedPath);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ── SaveFileAtPathAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveFileAtPathAsync_ShouldRoundTrip()
    {
        await using var source = new MemoryStream([10, 20, 30]);
        var storagePath = "thumbnails/test-item.jpg";

        var savedPath = await _service.SaveFileAtPathAsync(source, storagePath, "image/jpeg");

        savedPath.Should().Be(storagePath);

        await using var readStream = await _service.GetFileAsync(savedPath);
        using var reader = new MemoryStream();
        await readStream.CopyToAsync(reader);
        reader.ToArray().Should().Equal([10, 20, 30]);
    }

    [Fact]
    public async Task SaveFileAtPathAsync_WithBackslashPath_ShouldNormalize()
    {
        // 反斜杠应被规范化为正斜杠，读取时路径一致
        await using var source = new MemoryStream([5, 6]);
        var inputPath = @"thumbnails\backslash-test.jpg";
        var expectedPath = "thumbnails/backslash-test.jpg";

        var savedPath = await _service.SaveFileAtPathAsync(source, inputPath, "image/jpeg");

        savedPath.Should().Be(expectedPath);
        // 能通过规范化后的路径读取
        await using var readStream = await _service.GetFileAsync(expectedPath);
        readStream.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveFileAtPathAsync_WithPathTraversal_ShouldThrow()
    {
        // 包含 ../ 的路径尝试越界，应抛出异常
        await using var source = new MemoryStream([7]);
        var traversalPath = "../../etc/passwd";

        var act = async () => await _service.SaveFileAtPathAsync(source, traversalPath, "text/plain");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid storage path*");
    }

    [Fact]
    public async Task GetFileAsync_WithPathTraversal_ShouldThrow()
    {
        // GetFileAsync 同样受 basePath 前缀校验保护，越界路径应抛出异常
        var traversalPath = "../../etc/passwd";

        var act = async () => await _service.GetFileAsync(traversalPath);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid storage path*");
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, true);
        }
    }
}
