using AnyDrop.App.Platform;

namespace AnyDrop.App.Services;

/// <summary>
/// 基于 MAUI <see cref="Preferences"/> 的 <see cref="IPreferenceStore"/> 实现（平台适配层）。
/// </summary>
public sealed class MauiPreferenceStore : IPreferenceStore
{
    public string? Get(string key, string? defaultValue = null)
    {
        // Preferences 无法区分「键不存在」和「值为空字符串」，两者在这里都视为缺省。
        var value = Preferences.Get(key, defaultValue ?? string.Empty);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    public void Set(string key, string value) => Preferences.Set(key, value);
}
