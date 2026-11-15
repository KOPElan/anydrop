using System.Xml.Linq;
using FluentAssertions;

namespace AnyDrop.Tests.Unit;

/// <summary>
/// S3-06：包名与清单占位符的源码一致性检查。
///
/// MAUI 在构建时用 csproj 的 ApplicationId / ApplicationDisplayVersion /
/// ApplicationVersion 覆盖各平台清单里的对应值；源码里残留的旧值或手写值
/// 只会误导人（S2-09 已修过一次 package 不一致）。真正的构建产物
/// （apk/ipa/msix 里的清单）只能在 CI 的平台构建上核对，本机无 workload，
/// 见 docs/platform-manifests.md。
/// </summary>
public class ManifestConsistencyTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AnyDrop", "appsettings.json")))
                return dir.FullName;
        }
        throw new InvalidOperationException("未找到仓库根目录（向上 10 层内没有 AnyDrop/appsettings.json）。");
    }

    private static XDocument LoadXml(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        return XDocument.Load(path);
    }

    [Fact]
    public void AndroidManifest_PackageAndVersionCode_MatchCsproj()
    {
        var csproj = LoadXml(Path.Combine("AnyDrop.App", "AnyDrop.App.csproj"));
        var applicationId = csproj.Descendants("ApplicationId").Single().Value;
        var applicationVersion = csproj.Descendants("ApplicationVersion").Single().Value;

        var androidNs = XNamespace.Get("http://schemas.android.com/apk/res/android");
        var manifest = LoadXml(Path.Combine("AnyDrop.App", "Platforms", "Android", "AndroidManifest.xml"));
        var root = manifest.Root!;
        root.Attribute("package")!.Value.Should().Be(applicationId,
            "AndroidManifest 的 package 必须与 csproj ApplicationId 一致（构建以后者为准，旧值会误导）");
        root.Attribute(androidNs + "versionCode")!.Value.Should().Be(applicationVersion);
    }

    [Fact]
    public void Csproj_DisplayVersion_ComesFromSingleVersionSource()
    {
        // S3-01：显示版本唯一来源是 Git 标签（Directory.Build.props 的 $(Version)），
        // csproj 不得再手写版本号，否则标签与包版本分叉。
        var csproj = LoadXml(Path.Combine("AnyDrop.App", "AnyDrop.App.csproj"));
        csproj.Descendants("ApplicationDisplayVersion").Single().Value.Should().Be("$(Version)");
    }

    [Theory]
    [InlineData("Platforms/iOS/Info.plist")]
    [InlineData("Platforms/MacCatalyst/Info.plist")]
    public void AppleInfoPlist_HasNoHardcodedBundleIdentity(string relativePath)
    {
        // MAUI 构建时从 csproj 生成 CFBundleIdentifier / ShortVersionString /
        // Version / Name；手写值会被覆盖或冲突，所以源码里必须没有。
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), "AnyDrop.App", relativePath));
        text.Should().NotContain("<key>CFBundleIdentifier</key>");
        text.Should().NotContain("<key>CFBundleShortVersionString</key>");
        text.Should().NotContain("<key>CFBundleVersion</key>");
        text.Should().NotContain("<key>CFBundleName</key>");
    }

    [Fact]
    public void WindowsAppxManifest_KeepsMauiPlaceholders()
    {
        // Windows 清单里的 Identity / DisplayName / Logo 都是 MAUI 构建时替换的
        // 占位符（$placeholder$ / maui-package-name-placeholder）；若有人手填了
        // 真实值，构建会覆盖或冲突，所以断言它们仍是占位符。
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), "AnyDrop.App", "Platforms", "Windows", "Package.appxmanifest"));
        text.Should().Contain("maui-package-name-placeholder");
        text.Should().Contain("$placeholder$");
        text.Should().NotContain("com.kopelan.anydrop", "包名由 csproj ApplicationId 在构建时注入，不应手写进清单");
    }
}
