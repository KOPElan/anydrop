using System.Reflection;

namespace AnyDrop.Services;

/// <summary>
/// S3-01（ENG-02）：运行中实例的版本号。
///
/// 来源是程序集的 <c>AssemblyInformationalVersionAttribute</c>，构建时由
/// <c>Directory.Build.props</c> 写入（默认 0.0.0-dev，发布构建由标签传入）；
/// 构建于 git 仓库内时 .NET SDK 自动把完整提交 SHA 追加到其后
///（形如 <c>1.0.0+abc1234…</c>），本地开发构建为 <c>0.0.0-dev+&lt;SHA&gt;</c>。
/// <c>/health</c> 回显本值，任一运行实例都可凭它查到对应标签与提交。
/// </summary>
public static class AppVersion
{
    private static readonly string _current = Resolve();

    public static string Current => _current;

    private static string Resolve()
    {
        // 本程序集优先：测试宿主里 EntryAssembly 是测试运行器（如 xunit
        // console 的 2.9.3），不是 AnyDrop，不能以它为准。
        var own = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(own))
        {
            return own;
        }

        var entry = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return string.IsNullOrWhiteSpace(entry) ? "unknown" : entry;
    }
}
