using System.Net;
using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.App.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

public class FileUploadServiceTests
{
    private static FileUploadService CreateSut(HttpResponseMessage response)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("api")).Returns(client);

        return new FileUploadService(factoryMock.Object);
    }

    private static ShareItemDto MakeFileItem(Guid topicId, string fileName, string mimeType) =>
        new(Guid.NewGuid(), topicId, ShareContentType.File, null, fileName, null, mimeType, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task UploadFileAsync_Success_ReturnsShareItem()
    {
        var topicId = Guid.NewGuid();
        var item = MakeFileItem(topicId, "test.txt", "text/plain");
        var apiResponse = new ApiResponse<ShareItemDto>(true, item, null);
        var http = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        using var stream = new MemoryStream("hello"u8.ToArray());
        var result = await sut.UploadFileAsync(stream, "test.txt", "text/plain", topicId);

        result.FileName.Should().Be("test.txt");
        result.TopicId.Should().Be(topicId);
    }

    [Fact]
    public async Task UploadFileAsync_ReportsProgressOnCompletion()
    {
        var topicId = Guid.NewGuid();
        var item = MakeFileItem(topicId, "file.bin", "application/octet-stream");
        var apiResponse = new ApiResponse<ShareItemDto>(true, item, null);
        var http = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        double? reportedProgress = null;
        var progress = new Progress<double>(p => reportedProgress = p);

        using var stream = new MemoryStream([1, 2, 3]);
        await sut.UploadFileAsync(stream, "file.bin", "application/octet-stream", topicId, progress);

        await Task.Delay(50); // let Progress<T> fire on captured context
        reportedProgress.Should().Be(1.0);
    }

    [Fact]
    public async Task UploadFileAsync_NullStream_ThrowsArgumentNullException()
    {
        var sut = CreateSut(new HttpResponseMessage(HttpStatusCode.OK));

        var act = () => sut.UploadFileAsync(null!, "file.txt", "text/plain", Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UploadFileAsync_EmptyFileName_ThrowsArgumentException()
    {
        var sut = CreateSut(new HttpResponseMessage(HttpStatusCode.OK));

        var act = () => sut.UploadFileAsync(new MemoryStream(), "", "text/plain", Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
