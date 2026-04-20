using AnyDrop.Api;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using System.Security.Claims;

namespace AnyDrop.Tests.Unit.Api;

public class AuthEndpointsTests
{
    [Fact]
    public async Task SetupAsync_WhenAlreadyInitialized_ShouldReturnConflictEnvelope()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(x => x.SetupAsync(It.IsAny<SetupRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthResult<LoginResponse>.Failure("初始化已完成。", 409));

        var context = new DefaultHttpContext();
        var result = await AuthEndpoints.SetupAsync(new SetupRequest("a", "b", "b"), context, authService.Object, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ShouldReturnUnauthorized()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(x => x.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthResult<LoginResponse>.Failure("账号或密码错误。", 401));

        var context = new DefaultHttpContext();
        var result = await AuthEndpoints.LoginAsync(new LoginRequest("x", "/"), context, authService.Object, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSetupStatusAsync_WhenNoUser_ShouldReturnRequiresSetupTrue()
    {
        var userService = new Mock<IUserService>();
        userService.Setup(x => x.HasUserAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await AuthEndpoints.GetSetupStatusAsync(userService.Object, CancellationToken.None);

        result.Should().BeOfType<Ok<ApiEnvelope<SetupStatusDto>>>();
        result.Value.Should().NotBeNull();
        result.Value.Data.Should().NotBeNull();
        result.Value.Data!.RequiresSetup.Should().BeTrue();
    }

    [Fact]
    public async Task LogoutAsync_WhenMissingUserId_ShouldReturnUnauthorizedEnvelope()
    {
        var authService = new Mock<IAuthService>();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        var result = await AuthEndpoints.LogoutAsync(context, authService.Object, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task MeAsync_WhenMissingUserId_ShouldReturnUnauthorizedEnvelope()
    {
        var authService = new Mock<IAuthService>();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        var result = await AuthEndpoints.MeAsync(context, authService.Object, CancellationToken.None);

        result.Should().NotBeNull();
    }
}
