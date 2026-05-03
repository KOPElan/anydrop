using System.Net;
using System.Net.Http.Json;
using AnyDrop.App.Infrastructure;
using AnyDrop.App.Models;
using AnyDrop.App.Services;
using AnyDrop.Shared;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

public class AuthServiceTests
{
    private readonly Mock<ISecureTokenStorage> _tokenStorageMock = new();

    private (AuthService sut, Mock<HttpMessageHandler> handlerMock) CreateSut(HttpResponseMessage response)
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

        return (new AuthService(factoryMock.Object, _tokenStorageMock.Object), handlerMock);
    }

    [Fact]
    public async Task LoginAsync_SuccessResponse_SavesToken()
    {
        var userProfile = new UserProfileDto("TestUser");
        var loginResponse = new LoginResponse(userProfile, "jwt-token", DateTimeOffset.UtcNow.AddHours(1));
        var apiResponse = new ApiEnvelope<LoginResponse>(true, loginResponse, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(apiResponse)
        };
        var (sut, _) = CreateSut(http);

        var result = await sut.LoginAsync(new LoginRequest("pass123"));

        result.Success.Should().BeTrue();
        _tokenStorageMock.Verify(t => t.SaveTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_FailureResponse_DoesNotSaveToken()
    {
        var apiResponse = new ApiEnvelope<LoginResponse>(false, null, "Invalid");
        var http = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = JsonContent.Create(apiResponse)
        };
        var (sut, _) = CreateSut(http);

        var result = await sut.LoginAsync(new LoginRequest("wrong"));

        result.Success.Should().BeFalse();
        _tokenStorageMock.Verify(t => t.SaveTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAsync_ClearsToken()
    {
        var http = new HttpResponseMessage(HttpStatusCode.OK);
        var (sut, _) = CreateSut(http);

        await sut.LogoutAsync();

        _tokenStorageMock.Verify(t => t.ClearTokenAsync(), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WhenApiThrows_StillClearsToken()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network error"));

        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("api")).Returns(client);

        var sut = new AuthService(factoryMock.Object, _tokenStorageMock.Object);

        await sut.LogoutAsync();

        _tokenStorageMock.Verify(t => t.ClearTokenAsync(), Times.Once);
    }

    [Fact]
    public async Task SetupAsync_SuccessResponse_SavesToken()
    {
        var userProfile = new UserProfileDto("Admin");
        var loginResponse = new LoginResponse(userProfile, "jwt-token", DateTimeOffset.UtcNow.AddHours(1));
        var apiResponse = new ApiEnvelope<LoginResponse>(true, loginResponse, null);
        var http = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(apiResponse)
        };
        var (sut, _) = CreateSut(http);

        var result = await sut.SetupAsync(new SetupRequest("Admin", "pass1!", "pass1!"));

        result.Success.Should().BeTrue();
        _tokenStorageMock.Verify(t => t.SaveTokenAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Once);
    }
}

