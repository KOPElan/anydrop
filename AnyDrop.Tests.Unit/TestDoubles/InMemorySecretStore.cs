using System.Collections.Concurrent;
using AnyDrop.App.Platform;

namespace AnyDrop.Tests.Unit.TestDoubles;

/// <summary>
/// 测试用的内存密钥存储，替代 MAUI 的 <c>SecureStorage</c>。
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    /// <summary>置为 true 时 <see cref="GetAsync"/> 抛异常，用于验证重试语义。</summary>
    public bool ThrowOnGet { get; set; }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (ThrowOnGet)
        {
            throw new InvalidOperationException("simulated keystore failure");
        }

        _values.TryGetValue(key, out var value);
        return Task.FromResult<string?>(value);
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
