using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Services;

public class UserServiceTests
{
    [Fact]
    public async Task UpdateNicknameAsync_ValidNickname_ShouldPersist()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var user = new User { Nickname = "Old", PasswordHash = "h", PasswordSalt = "s" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new UserService(factory);
        var result = await sut.UpdateNicknameAsync(user.Id, "NewName");

        result.Succeeded.Should().BeTrue();
        // 全新上下文验证：播种用的上下文仍跟踪着旧实体，会返回过期值。
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.Users.SingleAsync()).Nickname.Should().Be("NewName");
    }

    [Fact]
    public async Task UpdateNicknameAsync_InvalidNickname_ShouldReturnBadRequest()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var user = new User { Nickname = "Old", PasswordHash = "h", PasswordSalt = "s" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new UserService(factory);
        var result = await sut.UpdateNicknameAsync(user.Id, " ");

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    private static TestDbContextFactory CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-user-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }
}
