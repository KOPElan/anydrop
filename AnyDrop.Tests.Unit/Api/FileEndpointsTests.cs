using AnyDrop.Api;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Moq;

namespace AnyDrop.Tests.Unit.Api;

public class FileEndpointsTests
{
    private static Mock<IFormFile> CreateFile(long length = 1024, string fileName = "a.bin")
    {
        var file = new Mock<IFormFile>();
        file.SetupGet(f => f.Length).Returns(length);
        file.SetupGet(f => f.FileName).Returns(fileName);
        file.SetupGet(f => f.ContentType).Returns("application/octet-stream");
        return file;
    }

    private static IConfiguration CreateConfiguration(long? maxFileSizeBytes = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:MaxFileSizeBytes"] = maxFileSizeBytes?.ToString()
            })
            .Build();

    [Fact]
    public async Task UploadFileAsync_WhenStorageIsFull_Returns507AndDoesNotSave()
    {
        // 磁盘写满时应在写入前拒绝，而不是写到一半失败
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.HasFreeSpace(It.IsAny<long>())).Returns(false);
        var shareService = new Mock<IShareService>();

        var result = await FileEndpoints.UploadFileAsync(
            CreateFile().Object,
            Guid.NewGuid(),
            burnAfterReading: false,
            shareService.Object,
            storage.Object,
            CreateConfiguration(),
            CancellationToken.None);

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status507InsufficientStorage);
        shareService.Verify(
            s => s.SendFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<long?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadFileAsync_WhenTopicMissing_Returns400()
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.HasFreeSpace(It.IsAny<long>())).Returns(true);

        var result = await FileEndpoints.UploadFileAsync(
            CreateFile().Object,
            topicId: null,
            burnAfterReading: false,
            new Mock<IShareService>().Object,
            storage.Object,
            CreateConfiguration(),
            CancellationToken.None);

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UploadFileAsync_WhenFileExceedsLimit_Returns400BeforeCheckingStorage()
    {
        var storage = new Mock<IFileStorageService>();
        var result = await FileEndpoints.UploadFileAsync(
            CreateFile(length: 2048).Object,
            Guid.NewGuid(),
            burnAfterReading: false,
            new Mock<IShareService>().Object,
            storage.Object,
            CreateConfiguration(maxFileSizeBytes: 1024),
            CancellationToken.None);

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        // 大小超限应在检查磁盘空间之前短路
        storage.Verify(s => s.HasFreeSpace(It.IsAny<long>()), Times.Never);
    }
}
