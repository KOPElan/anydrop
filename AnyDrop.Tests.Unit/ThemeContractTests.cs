using AnyDrop.Services;
using AnyDrop.Shared;
using FluentAssertions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;

namespace AnyDrop.Tests.Unit;

/// <summary>
/// S3-06：主题切换的三处重复实现合并为共享契约。
/// <list type="bullet">
/// <item>JS 侧：Web（AnyDrop/wwwroot/js/auth-interop.js）与 MAUI
/// （AnyDrop.App/wwwroot/index.html）的 <c>window.AnyDropTheme</c> 块必须字节一致；</item>
/// <item>C# 侧：两端都经 <see cref="IThemeService"/>，Web 实现直通同名 JS 调用。</item>
/// </list>
/// </summary>
public class ThemeContractTests
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

    /// <summary>从文件文本中截取 <c>window.AnyDropTheme = { ... };</c> 块（按花括号配对）。</summary>
    private static string ExtractThemeBlock(string text)
    {
        const string marker = "window.AnyDropTheme = {";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "文件应包含 window.AnyDropTheme 定义");

        var depth = 0;
        for (var i = start + marker.Length - 1; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    // 块以 "};" 结束
                    var end = i + 1;
                    if (end < text.Length && text[end] == ';') end++;
                    return text[start..end];
                }
            }
        }
        throw new InvalidOperationException("window.AnyDropTheme 块的花括号不配对。");
    }

    [Fact]
    public void ThemeJsBlock_IsIdenticalOnWebAndMaui()
    {
        var root = FindRepoRoot();
        var web = File.ReadAllText(Path.Combine(root, "AnyDrop", "wwwroot", "js", "auth-interop.js"));
        var maui = File.ReadAllText(Path.Combine(root, "AnyDrop.App", "wwwroot", "index.html"));

        var webBlock = ExtractThemeBlock(web);
        var mauiBlock = ExtractThemeBlock(maui);

        mauiBlock.Should().Be(webBlock,
            "两端的 window.AnyDropTheme 实现必须完全一致，改一处必须改两处（S3-06）");
    }

    [Fact]
    public void ThemeJsBlock_UsesSharedStorageKey()
    {
        var root = FindRepoRoot();
        var web = File.ReadAllText(Path.Combine(root, "AnyDrop", "wwwroot", "js", "auth-interop.js"));
        var block = ExtractThemeBlock(web);

        block.Should().Contain("'anydrop_theme'");
        block.Should().NotContain("'theme'");
        block.Should().Contain("init: function")
            .And.Contain("set: function")
            .And.Contain("get: function");
    }

    [Fact]
    public async Task JsThemeService_GetIsDark_CallsAnyDropThemeGet()
    {
        // 注意：InvokeAsync<T>(string, CancellationToken, object?[]) 是扩展方法，
        // 实际走到 IJSRuntime 的三参重载；Mock 必须按接口方法设。
        var js = new Mock<IJSRuntime>();
        js.Setup(x => x.InvokeAsync<bool>("AnyDropTheme.get", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<bool>(true));
        var sut = new JsThemeService(js.Object);

        (await sut.GetIsDarkAsync()).Should().BeTrue();
        js.Verify(x => x.InvokeAsync<bool>("AnyDropTheme.get", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()), Times.Once);
    }

    [Fact]
    public async Task JsThemeService_SetIsDark_CallsAnyDropThemeSetWithBool()
    {
        // 注意：InvokeVoidAsync 是扩展方法，底层走 InvokeAsync<IJSVoidResult>。
        var js = new Mock<IJSRuntime>();
        js.Setup(x => x.InvokeAsync<IJSVoidResult>("AnyDropTheme.set", It.IsAny<CancellationToken>(), It.IsAny<object?[]>()))
            .Returns(new ValueTask<IJSVoidResult>(Mock.Of<IJSVoidResult>()));
        var sut = new JsThemeService(js.Object);

        await sut.SetIsDarkAsync(true);

        js.Verify(x => x.InvokeAsync<IJSVoidResult>("AnyDropTheme.set",
            It.IsAny<CancellationToken>(),
            It.Is<object?[]>(args => args.Length == 1 && Equals(args[0], true))), Times.Once);
    }
}
