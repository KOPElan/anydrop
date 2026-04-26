namespace AnyDrop.Models;

/// <summary>应用支持的语言代码常量，集中管理以避免重复定义。</summary>
public static class SupportedLanguages
{
    public const string ZhCN = "zh-CN";
    public const string ZhTW = "zh-TW";
    public const string En = "en";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ZhCN, ZhTW, En
    };
}
