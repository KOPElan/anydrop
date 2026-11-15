using AnyDrop.Shared;

namespace AnyDrop.App.Services;

/// <summary>
/// 服务端错误码 → 界面文案（S3-06）。
///
/// 服务端信封带稳定的 <see cref="ApiErrorCodes"/>；客户端不再直接显示
/// 中文原文。查不到码对应的本地化文案时回退显示服务端原文
///（旧服务端、或新码尚未翻译时界面仍有文字可看）。
/// </summary>
public static class ApiErrorMessages
{
    /// <summary>
    /// 按错误码查本地化文案。
    /// <paramref name="lookup"/> 由调用方提供（通常是
    /// <c>key => { var s = L[key]; return s.ResourceNotFound ? null : s.Value; }</c>，
    /// 其中 L 为 <c>IStringLocalizer&lt;AppStrings&gt;</c>）。
    /// </summary>
    /// <returns>本地化文案；无码或查不到时返回 <paramref name="fallback"/>（可为 null）。</returns>
    public static string? Localize(string? code, Func<string, string?> lookup, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var localized = lookup(ApiErrorCodes.ResourceKey(code));
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }
        }

        return fallback;
    }
}
