namespace AnyDrop.App.Services;

/// <summary>语言选项。</summary>
/// <param name="Code">BCP-47 语言代码，例如 "zh-CN"。</param>
/// <param name="NativeName">以目标语言书写的名称，例如 "简体中文"。</param>
public sealed record LanguageOption(string Code, string NativeName);

/// <summary>应用内语言管理接口。</summary>
public interface ILocalizationService
{
    /// <summary>当前生效的语言代码（BCP-47）。</summary>
    string CurrentLanguage { get; }

    /// <summary>应用支持的全部语言列表。</summary>
    IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    /// <summary>
    /// 切换应用界面语言并持久化到本地存储。
    /// 调用后需由调用方执行页面重载（<c>Nav.NavigateTo(uri, forceLoad: true)</c>）以让新语言在所有组件中生效。
    /// </summary>
    /// <param name="languageCode">目标语言代码（必须在 <see cref="SupportedLanguages"/> 中）。</param>
    void SetLanguage(string languageCode);
}
