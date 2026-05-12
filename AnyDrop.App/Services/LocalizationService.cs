using System.Globalization;

namespace AnyDrop.App.Services;

/// <summary>
/// 应用内语言管理服务实现。
/// 支持简体中文（zh-CN）、繁體中文（zh-TW）和 English（en）三种语言，
/// 默认以系统语言为主，用户手动切换后通过 <see cref="Microsoft.Maui.Storage.Preferences"/> 持久化。
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private const string PrefKey = "anydrop_language";

    /// <inheritdoc />
    public string CurrentLanguage { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("zh-CN", "简体中文"),
        new("zh-TW", "繁體中文"),
        new("en", "English"),
    ];

    public LocalizationService()
    {
        CurrentLanguage = ResolveInitialLanguage();
        ApplyCulture(CurrentLanguage);
    }

    /// <inheritdoc />
    public void SetLanguage(string languageCode)
    {
        if (!IsSupported(languageCode)) return;

        CurrentLanguage = languageCode;

#if ANDROID || IOS || MACCATALYST || WINDOWS
        Microsoft.Maui.Storage.Preferences.Set(PrefKey, languageCode);
#endif

        ApplyCulture(languageCode);
    }

    // ── 内部方法 ─────────────────────────────────────────────────

    private static string ResolveInitialLanguage()
    {
        string? stored = null;

#if ANDROID || IOS || MACCATALYST || WINDOWS
        stored = Microsoft.Maui.Storage.Preferences.Get(PrefKey, null);
#endif

        // 如果有合法的已存储语言，直接使用
        if (!string.IsNullOrEmpty(stored) && IsSupported(stored))
            return stored;

        // 否则尝试匹配系统语言
        return NormalizeToSupported(CultureInfo.CurrentUICulture.Name);
    }

    private static void ApplyCulture(string languageCode)
    {
        var culture = new CultureInfo(languageCode);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static bool IsSupported(string code) =>
        code is "zh-CN" or "zh-TW" or "en";

    /// <summary>
    /// 将任意 BCP-47 语言代码归一化为应用支持的三种语言之一。
    /// 未能匹配时回退为 <c>zh-CN</c>。
    /// </summary>
    internal static string NormalizeToSupported(string cultureName)
    {
        if (cultureName.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
            || cultureName.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || cultureName.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
            || cultureName.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase))
            return "zh-TW";

        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";

        if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return "en";

        return "zh-CN";
    }
}
