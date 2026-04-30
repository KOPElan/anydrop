using System.Net;
using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.App.Services;
using AnyDrop.Shared;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

public class SettingsServiceTests
{
    private static SettingsService CreateSut(HttpResponseMessage response)
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

        return new SettingsService(factoryMock.Object);
    }

    [Fact]
    public async Task GetSecuritySettingsAsync_ReturnsData()
    {
        // 与服务端 SecuritySettingsDto 一致：(AutoFetchLinkPreview, BurnAfterReadingMinutes, Language, AutoCleanupEnabled, AutoCleanupMonths)
        var settings = new SecuritySettingsDto(true, 5, "zh-CN", true, 24);
        var apiResponse = new ApiResponse<SecuritySettingsDto>(true, settings, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        var result = await sut.GetSecuritySettingsAsync();

        result.AutoFetchLinkPreview.Should().BeTrue();
        result.BurnAfterReadingMinutes.Should().Be(5);
        result.AutoCleanupEnabled.Should().BeTrue();
        result.AutoCleanupMonths.Should().Be(24);
    }

    [Fact]
    public async Task GetSecuritySettingsAsync_OnError_ReturnsDefaults()
    {
        var http = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}")
        };
        var sut = CreateSut(http);

        var result = await sut.GetSecuritySettingsAsync();

        result.Should().NotBeNull();
        result.BurnAfterReadingMinutes.Should().Be(0);
    }

    [Fact]
    public async Task UpdateNicknameAsync_SendsPutRequest()
    {
        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("api")).Returns(client);
        var sut = new SettingsService(factoryMock.Object);

        await sut.UpdateNicknameAsync(new UpdateNicknameRequest("NewName"));

        capturedRequest!.Method.Should().Be(HttpMethod.Put);
        capturedRequest.RequestUri!.PathAndQuery.Should().Contain("profile");
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenBadRequest_ThrowsHttpRequestException()
    {
        var http = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var sut = CreateSut(http);

        var act = () => sut.UpdatePasswordAsync(new UpdatePasswordRequest("wrong", "newpass", "newpass"));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_ReturnsDeletedCount()
    {
        // 服务端返回 CleanupResult { DeletedCount: 42 }，不是直接的 int
        var cleanupResult = new CleanupResult(42);
        var apiResponse = new ApiResponse<CleanupResult>(true, cleanupResult, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        var result = await sut.CleanupOldMessagesAsync(3);

        result.Should().Be(42);
    }
}

