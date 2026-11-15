using AnyDrop.Logging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Tests.Unit.Logging;

/// <summary>S3-04：文件日志（写入、滚动、保留期、配置解析）。</summary>
public class FileLoggerProviderTests : IDisposable
{
    private readonly string _logDir =
        Path.Combine(AppContext.BaseDirectory, "test-filelog", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_WritesLineToDailyUtcFile()
    {
        var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = _logDir });
        var logger = provider.CreateLogger("AnyDrop.Services.ShareService");

        logger.LogInformation("hello {Name}", "world");
        logger.LogWarning(new InvalidOperationException("boom"), "something failed");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var path = Path.Combine(_logDir, $"anydrop-{today:yyyyMMdd}.log");
        var text = File.ReadAllText(path);
        text.Should().Contain("[Information] AnyDrop.Services.ShareService: hello world");
        text.Should().Contain("[Warning] AnyDrop.Services.ShareService: something failed");
        text.Should().Contain("InvalidOperationException");
        text.Should().Contain("boom");
        provider.Dispose();
    }

    [Fact]
    public void Write_WhenFileExceedsLimit_RollsToNumberedFile()
    {
        var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = _logDir,
            MaxFileSizeMB = 1,
        });
        var logger = provider.CreateLogger("Test");

        // 每行约 200 字节，6000 行约 1.2MB，超过 1MB 上限后必须滚动。
        var line = new string('x', 200);
        for (var i = 0; i < 6000; i++)
        {
            logger.LogInformation("{Line}", line);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        File.Exists(Path.Combine(_logDir, $"anydrop-{today:yyyyMMdd}.1.log")).Should().BeTrue();
        provider.Dispose();
    }

    [Fact]
    public void Write_PruneOldFiles_DeletesFilesOlderThanRetainedDays()
    {
        Directory.CreateDirectory(_logDir);
        var oldFile = Path.Combine(_logDir, "anydrop-20200101.log");
        File.WriteAllText(oldFile, "old");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-30));

        var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = _logDir,
            RetainedDays = 14,
        });
        provider.CreateLogger("Test").LogInformation("new entry");

        File.Exists(oldFile).Should().BeFalse();
        provider.Dispose();
    }

    [Fact]
    public void Write_WhenDisabled_WritesNothing()
    {
        var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = _logDir,
            Enabled = false,
        });
        provider.CreateLogger("Test").LogInformation("should not be written");

        Directory.Exists(_logDir).Should().BeFalse();
        provider.Dispose();
    }

    [Fact]
    public void ResolveOptions_DefaultDirectory_DerivesFromDatabasePath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DatabasePath"] = Path.Combine("data", "anydrop.db"),
            })
            .Build();

        var options = FileLogging.ResolveOptions(configuration);

        options.Directory.Should().Be(Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(Path.Combine("data", "anydrop.db")))!,
            "logs"));
        options.MaxFileSizeMB.Should().Be(10);
        options.RetainedDays.Should().Be(14);
    }

    [Fact]
    public void ResolveOptions_ConfiguredDirectory_WinsOverDerived()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DatabasePath"] = Path.Combine("data", "anydrop.db"),
                ["Logging:File:Directory"] = "/var/log/anydrop",
            })
            .Build();

        FileLogging.ResolveOptions(configuration).Directory.Should().Be("/var/log/anydrop");
    }

    [Theory]
    [InlineData("Logging:File:MaxFileSizeMB", "0")]
    [InlineData("Logging:File:MaxFileSizeMB", "101")]
    [InlineData("Logging:File:RetainedDays", "0")]
    [InlineData("Logging:File:RetainedDays", "91")]
    public void ResolveOptions_OutOfBounds_Throws(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        var act = () => FileLogging.ResolveOptions(configuration);
        act.Should().Throw<InvalidOperationException>();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_logDir, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响测试结果。
        }
    }
}
