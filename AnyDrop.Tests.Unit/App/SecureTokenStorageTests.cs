using AnyDrop.App.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.App;

/// <summary>
/// S2-01 / S2-08：Refresh Token 存储往返、过期判定、密钥库故障时保存/清除不抛错。
/// </summary>
public class SecureTokenStorageTests
{
    [Fact]
    public async Task RefreshToken_RoundTrip()
    {
        var storage = new SecureTokenStorage(new InMemorySecretStore());

        await storage.SaveRefreshTokenAsync("refresh-1", DateTimeOffset.UtcNow.AddDays(30));

        (await storage.GetRefreshTokenAsync()).Should().Be("refresh-1");

        await storage.ClearRefreshTokenAsync();

        (await storage.GetRefreshTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task RefreshToken_Expired_ReturnsNull()
    {
        var storage = new SecureTokenStorage(new InMemorySecretStore());

        await storage.SaveRefreshTokenAsync("refresh-1", DateTimeOffset.UtcNow.AddMinutes(-1));

        (await storage.GetRefreshTokenAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetTokenExpiresAtAsync_ReturnsSavedExpiry()
    {
        var storage = new SecureTokenStorage(new InMemorySecretStore());
        var expires = DateTimeOffset.UtcNow.AddHours(3);

        await storage.SaveTokenAsync("access", expires);

        (await storage.GetTokenExpiresAtAsync()).Should().Be(expires);
    }

    [Fact]
    public async Task SaveTokenAsync_WhenSecretStoreThrows_DoesNotThrowAndKeepsMemoryCache()
    {
        var store = new InMemorySecretStore { ThrowOnSet = true };
        var storage = new SecureTokenStorage(store);

        var save = async () => await storage.SaveTokenAsync("access", DateTimeOffset.UtcNow.AddHours(1));
        await save.Should().NotThrowAsync("S2-08：密钥库写入失败不得打断登录流程");

        (await storage.GetTokenAsync()).Should().Be("access", "内存缓存仍应可用");
    }

    [Fact]
    public async Task ClearTokenAsync_WhenSecretStoreThrows_DoesNotThrowAndClearsMemoryCache()
    {
        var store = new InMemorySecretStore();
        var storage = new SecureTokenStorage(store);
        await storage.SaveTokenAsync("access", DateTimeOffset.UtcNow.AddHours(1));
        store.ThrowOnRemove = true;

        var clear = async () => await storage.ClearTokenAsync();
        await clear.Should().NotThrowAsync("S2-08：密钥库清除失败不得打断登出流程");

        (await storage.GetTokenAsync()).Should().BeNull();
    }
}
