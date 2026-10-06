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

        _service = new LocalFileStorageService(configuration, TimeProvider.System);
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

    // ── 原子写入与剩余空间 ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveFileAsync_WhenSourceStreamFails_LeavesNoFileBehind()
    {
        // 直接写目标路径时，中途失败会在最终位置留下半截文件。
        // 现在先写临时文件再原子改名，因此失败后不应留下任何产物。
        var act = () => _service.SaveFileAsync(
            new FailingReadStream(), "a.bin", "application/octet-stream");

        await act.Should().ThrowAsync<IOException>();

        var leftovers = Directory.Exists(_basePath)
            ? Directory.GetFiles(_basePath, "*", SearchOption.AllDirectories)
            : [];

        leftovers.Should().BeEmpty("失败的上传不应留下临时文件或半截文件");
    }

    [Fact]
    public async Task SaveFileAsync_OnSuccess_LeavesExactlyOneFinalFile()
    {
        await using var source = new MemoryStream([1, 2, 3]);

        var savedPath = await _service.SaveFileAsync(source, "a.bin", "application/octet-stream");

        await using var read = await _service.GetFileAsync(savedPath);
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal([1, 2, 3]);

        var files = Directory.GetFiles(_basePath, "*", SearchOption.AllDirectories);
        files.Should().ContainSingle("临时文件应已被改名，不应残留");
        files[0].Should().NotEndWith(".part");
    }

    [Fact]
    public void HasFreeSpace_ForSmallFile_IsTrue()
        => _service.HasFreeSpace(1024).Should().BeTrue();

    [Fact]
    public void HasFreeSpace_ForImpossiblyLargeFile_IsFalse()
        // 远超任何真实磁盘容量，必然超出「文件 + 预留余量」
        => _service.HasFreeSpace(long.MaxValue / 2).Should().BeFalse();

    /// <summary>读取即失败的流，用于验证写入失败时不会留下半截文件。</summary>
    private sealed class FailingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new IOException("simulated read failure");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromException<int>(new IOException("simulated read failure"));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, true);
        }
    }
}
