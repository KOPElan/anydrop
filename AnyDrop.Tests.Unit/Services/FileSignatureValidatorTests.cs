using AnyDrop.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S3-02：文件魔数校验。MIME 自报不可信，已知签名类型必须与文件头一致。
/// </summary>
public class FileSignatureValidatorTests
{
    [Theory]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 })]
    [InlineData("image/bmp", new byte[] { 0x42, 0x4D, 0x00, 0x00 })]
    [InlineData("video/mp4", new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D })]
    [InlineData("video/webm", new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x00 })]
    [InlineData("video/x-msvideo", new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x41, 0x56, 0x49, 0x20 })]
    [InlineData("audio/mpeg", new byte[] { 0x49, 0x44, 0x33, 0x03, 0x00 })]
    [InlineData("audio/wav", new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 })]
    [InlineData("application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })]
    [InlineData("application/zip", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 })]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    public void IsConsistent_MatchingSignature_ReturnsTrue(string mimeType, byte[] header)
        => FileSignatureValidator.IsConsistent(header, mimeType).Should().BeTrue();

    [Theory]
    // JPEG 字节冒充 PNG、PNG 字节冒充 JPEG、文本冒充图片、可执行文件冒充 PDF。
    [InlineData("image/png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData("image/jpeg", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("image/png", new byte[] { 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x77, 0x6F })]
    [InlineData("application/pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 })]
    [InlineData("video/mp4", new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 })]
    public void IsConsistent_MismatchedSignature_ReturnsFalse(string mimeType, byte[] header)
        => FileSignatureValidator.IsConsistent(header, mimeType).Should().BeFalse();

    [Theory]
    // 无可靠魔数的类型跳过检查（仍由 MIME 规范化 + 附件处置兜底）。
    [InlineData("text/plain", new byte[] { 0x68, 0x65, 0x6C, 0x6C, 0x6F })]
    [InlineData("image/svg+xml", new byte[] { 0x3C, 0x73, 0x76, 0x67 })]
    [InlineData("application/octet-stream", new byte[] { 0x01, 0x02, 0x03 })]
    [InlineData("application/octet-stream", new byte[] { })]
    [InlineData(null, new byte[] { 0x01, 0x02, 0x03 })]
    public void IsConsistent_UnknownMimeType_SkipsCheck(string? mimeType, byte[] header)
        => FileSignatureValidator.IsConsistent(header, mimeType).Should().BeTrue();

    [Fact]
    public void IsConsistent_HeaderShorterThanSignature_ReturnsFalse()
    {
        // 3 字节文件自称 PNG：合法 PNG 不会比签名还短。
        FileSignatureValidator.IsConsistent([0x89, 0x50, 0x4E], "image/png").Should().BeFalse();
    }

    [Fact]
    public void IsConsistent_MimeWithParameters_IsNormalizedFirst()
    {
        // S0-02 的规范化同样适用：带 charset 参数的 MIME 先去参数再比签名。
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        FileSignatureValidator.IsConsistent(png, "image/png; charset=utf-8").Should().BeTrue();
        FileSignatureValidator.IsConsistent(png, "IMAGE/PNG").Should().BeTrue();
    }

    [Fact]
    public async Task EnsureConsistentAsync_SeekableStream_RewindsToStart()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03 };
        await using var stream = new MemoryStream(png);

        var result = await FileSignatureValidator.EnsureConsistentAsync(stream, "image/png");

        result.Should().BeSameAs(stream);
        stream.Position.Should().Be(0);
        var all = new byte[png.Length];
        await stream.ReadExactlyAsync(all);
        all.Should().Equal(png);
    }

    [Fact]
    public async Task EnsureConsistentAsync_NonSeekableStream_PreservesBytes()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03 };
        await using var inner = new NonSeekableStream(new MemoryStream(png));

        var result = await FileSignatureValidator.EnsureConsistentAsync(inner, "image/png");

        var all = new byte[png.Length];
        await result.ReadExactlyAsync(all);
        all.Should().Equal(png);
    }

    [Fact]
    public async Task EnsureConsistentAsync_MismatchedSignature_ThrowsArgumentException()
    {
        await using var stream = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0x00, 0x00]);

        var act = () => FileSignatureValidator.EnsureConsistentAsync(stream, "image/png");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>把可 Seek 的流伪装成不可 Seek，用于验证前置缓冲包装。</summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
