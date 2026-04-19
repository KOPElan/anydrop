using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Services;

public class UserServiceTests
{
    [Fact]
    public async Task UpdateNicknameAsync_ValidNickname_ShouldPersist()
    {
        await using var db = CreateDbContext();
        var user = new User { Nickname = "Old", PasswordHash = "h", PasswordSalt = "s" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new UserService(db);
        var result = await sut.UpdateNicknameAsync(user.Id, "NewName");

        result.Succeeded.Should().BeTrue();
        (await db.Users.SingleAsync()).Nickname.Should().Be("NewName");
    }

    [Fact]
    public async Task UpdateNicknameAsync_InvalidNickname_ShouldReturnBadRequest()
    {
        await using var db = CreateDbContext();
        var user = new User { Nickname = "Old", PasswordHash = "h", PasswordSalt = "s" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new UserService(db);
        var result = await sut.UpdateNicknameAsync(user.Id, " ");

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-user-{Guid.NewGuid():N}")
            .Options;
        return new AnyDropDbContext(options);
    }
}
