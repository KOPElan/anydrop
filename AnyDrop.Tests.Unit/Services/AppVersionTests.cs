using AnyDrop.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S3-01（ENG-02）：版本号来自程序集 InformationalVersion，
/// 由 Directory.Build.props（默认 0.0.0-dev）与 Directory.Build.targets
///（追加提交短哈希）写入。测试宿主里 EntryAssembly 是测试运行器，
/// AppVersion 应退回本程序集，仍为非空值。
/// </summary>
public class AppVersionTests
{
    [Fact]
    public void Current_IsNotEmpty()
    {
        AppVersion.Current.Should().NotBeNullOrWhiteSpace();
        AppVersion.Current.Should().NotBe("unknown");
    }

    [Fact]
    public void Current_MatchesAssemblyInformationalVersion()
    {
        var expected = typeof(AppVersion).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .SingleOrDefault()
            ?.InformationalVersion;

        expected.Should().NotBeNullOrWhiteSpace();
        AppVersion.Current.Should().Be(expected);
    }
}
