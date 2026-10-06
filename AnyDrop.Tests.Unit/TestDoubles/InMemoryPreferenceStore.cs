using System.Collections.Concurrent;
using AnyDrop.App.Platform;

namespace AnyDrop.Tests.Unit.TestDoubles;

/// <summary>
/// 测试用的内存偏好存储，替代 MAUI 的 <c>Preferences</c>。
/// 语义与 <c>MauiPreferenceStore</c> 一致：空字符串视为缺省值。
/// </summary>
public sealed class InMemoryPreferenceStore : IPreferenceStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public string? Get(string key, string? defaultValue = null)
        => _values.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : defaultValue;

    public void Set(string key, string value) => _values[key] = value;
}
