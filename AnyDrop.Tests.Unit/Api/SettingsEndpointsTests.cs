using AnyDrop.Api;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Security.Claims;

namespace AnyDrop.Tests.Unit.Api;

public class SettingsEndpointsTests
{
    [Fact]
    public async Task GetSecurityAsync_ShouldReturnEnvelope()
    {
        var settingsService = new Mock<ISystemSettingsService>();
        settingsService.Setup(x => x.GetSecuritySettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecuritySettingsDto(true));

        var result = await SettingsEndpoints.GetSecurityAsync(settingsService.Object, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenAuthenticated_ShouldCallService()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(x => x.UpdateNicknameAsync(It.IsAny<Guid>(), It.IsAny<UpdateNicknameRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthResult<UserProfileDto>.Success(new UserProfileDto("New", null)));

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", Guid.NewGuid().ToString()), new Claim("sessionVersion", "1")], "test"))
        };

        var result = await SettingsEndpoints.UpdateProfileAsync(new UpdateNicknameRequest("New"), context, authService.Object, CancellationToken.None);

        result.Should().NotBeNull();
        authService.Verify(x => x.UpdateNicknameAsync(It.IsAny<Guid>(), It.IsAny<UpdateNicknameRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
