using AnyDrop.App.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.App;

/// <summary>S2-08：按扩展名推断 MIME 类型（选择器 ContentType 为空时的兜底）。</summary>
public class FileMimeTypesTests
{
    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.JPEG", "image/jpeg")]
    [InlineData("pic.png", "image/png")]
    [InlineData("anim.gif", "image/gif")]
    [InlineData("pic.webp", "image/webp")]
    [InlineData("pic.heic", "image/heic")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.MOV", "video/quicktime")]
    [InlineData("clip.webm", "video/webm")]
    [InlineData("doc.pdf", "application/pdf")]
    [InlineData("archive.zip", "application/zip")]
    [InlineData("note.txt", "text/plain")]
    public void FromFileName_KnownExtension_ReturnsMime(string fileName, string expected)
    {
        FileMimeTypes.FromFileName(fileName).Should().Be(expected);
    }

    [Theory]
    [InlineData("file.unknownext")]
    [InlineData("noextension")]
    [InlineData("")]
    [InlineData("   ")]
    public void FromFileName_UnknownOrMissing_ReturnsOctetStream(string fileName)
    {
        FileMimeTypes.FromFileName(fileName).Should().Be("application/octet-stream");
    }

    [Fact]
    public void FromFileName_Null_ReturnsOctetStream()
    {
        FileMimeTypes.FromFileName(null!).Should().Be("application/octet-stream");
    }
}
