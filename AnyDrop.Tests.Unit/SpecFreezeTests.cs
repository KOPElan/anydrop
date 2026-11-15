using AnyDrop.Services;
using AnyDrop.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnyDrop.Tests.Unit;

/// <summary>
/// S3-05：冻结常量一致性检查。规格冻结见 <c>docs/spec-freeze.md</c>：
/// 上传上限 500MB 与后台任务时区环境变量是 Elan 定稿的数，
/// 代码、配置文件、compose 与 .env 示例四处必须一致。
/// 改任一处而不改其余处，本测试即失败——这正是它存在的意义。
/// </summary>
public class SpecFreezeTests
{
    /// <summary>上传上限（D2）：500MB = 524288000 字节。</summary>
    public const long FrozenMaxFileSizeBytes = 524288000;

    [Fact]
    public void UploadLimit_CodeConstant_Is500MB()
    {
        UploadLimits.MaxFileSizeBytes.Should().Be(FrozenMaxFileSizeBytes);
    }

    [Fact]
    public void UploadLimit_ShippedAppSettings_Are500MB()
    {
        var root = FindRepoRoot();
        foreach (var relative in new[]
                 {
                     Path.Combine("AnyDrop", "appsettings.json"),
                     Path.Combine("AnyDrop", "appsettings.Development.json"),
                 })
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(root, relative), optional: false)
                .Build();
            configuration.GetValue<long?>("Storage:MaxFileSizeBytes")
                .Should().Be(FrozenMaxFileSizeBytes, $"{relative} 的 Storage:MaxFileSizeBytes 必须与冻结值一致");
        }
    }

    [Fact]
    public void UploadLimit_ComposeAndEnvExample_Are500MB()
    {
        var root = FindRepoRoot();
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        compose.Should().Contain("Storage__MaxFileSizeBytes=${ANYDROP_MAX_FILE_SIZE:-524288000}");

        var envExample = File.ReadAllText(Path.Combine(root, ".env.example"));
        envExample.Should().Contain("ANYDROP_MAX_FILE_SIZE=524288000");
    }

    [Fact]
    public void UploadLimit_NoLegacyLimitsRemain()
    {
        // 1GB 回退值与 100MB 开发值已随 D2 删除；若某处又出现，应先问再改。
        var root = FindRepoRoot();
        foreach (var relative in new[]
                 {
                     Path.Combine("AnyDrop", "appsettings.json"),
                     Path.Combine("AnyDrop", "appsettings.Development.json"),
                     "docker-compose.yml",
                     ".env.example",
                 })
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            text.Should().NotContain("1073741824", $"{relative} 不应再有 1GB 上限");
            text.Should().NotContain("104857600", $"{relative} 不应再有 100MB 上限");
        }
    }

    [Fact]
    public void BackgroundTaskTimeZone_EnvVar_ResolvesToConfiguredZone()
    {
        // D6：环境变量 BackgroundTasks__TimeZoneId（双下划线）映射到配置键
        // BackgroundTasks:TimeZoneId；未配置时用当前计算机时区，绝不静默 UTC。
        Environment.SetEnvironmentVariable("BackgroundTasks__TimeZoneId", "Asia/Shanghai");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            var zone = new BackgroundTaskTimeZone(
                configuration, NullLogger<BackgroundTaskTimeZone>.Instance);
            zone.TimeZone.Id.Should().Be("Asia/Shanghai");
            zone.Source.Should().Contain("BackgroundTasks:TimeZoneId=Asia/Shanghai");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BackgroundTasks__TimeZoneId", null);
        }
    }

    [Fact]
    public void BackgroundTaskTimeZone_Unset_UsesLocalMachineZone()
    {
        Environment.SetEnvironmentVariable("BackgroundTasks__TimeZoneId", null);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var zone = new BackgroundTaskTimeZone(
            configuration, NullLogger<BackgroundTaskTimeZone>.Instance);
        zone.TimeZone.Should().Be(TimeZoneInfo.Local);
    }

    /// <summary>
    /// 从测试二进制目录向上找仓库根（以 AnyDrop/appsettings.json 存在为准），
    /// 找不到时明确失败，不静默跳过。
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AnyDrop", "appsettings.json")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            "找不到仓库根（向上 10 级均无 AnyDrop/appsettings.json）；一致性检查无法执行。");
    }
}
