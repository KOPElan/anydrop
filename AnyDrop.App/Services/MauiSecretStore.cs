using AnyDrop.App.Platform;

namespace AnyDrop.App.Services;

/// <summary>
/// 基于 MAUI <see cref="SecureStorage"/> 的 <see cref="ISecretStore"/> 实现（平台适配层）。
/// Android 走 Keystore，iOS/macOS 走 Keychain，Windows 走 DPAPI。
/// </summary>
public sealed class MauiSecretStore : ISecretStore
{
    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => SecureStorage.GetAsync(key);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
        => SecureStorage.SetAsync(key, value);

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        SecureStorage.Remove(key);
        return Task.CompletedTask;
    }
}
