using AnyDrop.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Services;

public class PasswordHasherServiceTests
{
    [Fact]
    public void HashPassword_ThenVerify_ShouldReturnTrue()
    {
        var sut = new PasswordHasherService();
        var (hash, salt) = sut.HashPassword("P@ssw0rd!");

        var ok = sut.VerifyPassword("P@ssw0rd!", hash, salt);

        ok.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ShouldReturnFalse()
    {
        var sut = new PasswordHasherService();
        var (hash, salt) = sut.HashPassword("P@ssw0rd!");

        var ok = sut.VerifyPassword("Wrong", hash, salt);

        ok.Should().BeFalse();
    }
}
