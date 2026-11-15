namespace AnyDrop.Shared;

/// <summary>
/// 主题切换的 C# 契约（S3-06）。
///
/// Web 与 MAUI 两端共用同一接口：底层都是 JS 互操作调用
/// <c>window.AnyDropTheme</c>（见两端 JS 文件，内容由 ThemeContractTests
/// 保证一致）。MAUI 端额外把偏好镜像到原生 <c>IPreferenceStore</c>，
/// 差异收敛在各自的实现里，调用方只认这个接口。
/// </summary>
public interface IThemeService
{
    /// <summary>当前是否为暗色主题。</summary>
    Task<bool> GetIsDarkAsync(CancellationToken ct = default);

    /// <summary>应用并持久化主题偏好。</summary>
    Task SetIsDarkAsync(bool isDark, CancellationToken ct = default);
}
