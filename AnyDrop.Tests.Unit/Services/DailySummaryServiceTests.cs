using AnyDrop.Data;
using AnyDrop.Logging;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S3-04：每日运维摘要。真实 SQLite 文件 + 真实存储目录验证计数；
/// 日志失败计数用事先写好的日志文件验证窗口换算。
/// </summary>
public class DailySummaryServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, "test-daily-summary", Guid.NewGuid().ToString("N"));

    private string DbPath => Path.Combine(_root, "anydrop.db");
    private string BasePath => Path.Combine(_root, "files");
    private string LogDir => Path.Combine(_root, "logs");

    private IConfiguration CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:BasePath"] = BasePath,
        })
        .Build();

    private TestDbContextFactory CreateFactory()
    {
        Directory.CreateDirectory(_root);
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={DbPath}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private DailySummaryService CreateSut(TestDbContextFactory factory, FileLoggerOptions? logOptions = null)
    {
        var configuration = CreateConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbContextFactory<AnyDropDbContext>>(factory);
        services.AddSingleton(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IFileStorageService>(_ =>
            new LocalFileStorageService(configuration, TimeProvider.System));
        services.AddScoped<OrphanFileReconciler>();
        var provider = services.BuildServiceProvider();

        // 显式钉死 UTC，避免容器本机时区不同导致日志窗口换算漂移。
        var taskTimeZone = new BackgroundTaskTimeZone(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BackgroundTasks:TimeZoneId"] = "UTC",
                })
                .Build(),
            NullLogger<BackgroundTaskTimeZone>.Instance);
        return new DailySummaryService(
            provider,
            TimeProvider.System,
            taskTimeZone,
            logOptions ?? new FileLoggerOptions { Directory = LogDir },
            configuration,
            NullLogger<DailySummaryService>.Instance);
    }

    private async Task SeedAsync(TestDbContextFactory factory)
    {
        // 测试工厂只实现同步 CreateDbContext（见 TestDoubles/TestDbContextFactory）。
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        // 一个活文件消息（引用真实存在的文件）+ 一个孤儿文件（2 天前写入，未被引用）。
        Directory.CreateDirectory(BasePath);
        var kept = Path.Combine(BasePath, "kept.bin");
        File.WriteAllText(kept, "kept");
        var orphan = Path.Combine(BasePath, "orphan.bin");
        File.WriteAllText(orphan, "orphan");
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-2));

        db.ShareItems.Add(new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.File,
            Content = "kept.bin",
            CreatedAt = DateTimeOffset.UtcNow,
            UploadState = UploadState.Ready,
        });

        // 待删除队列里一条记录：文件真实存在、未被活行引用、刚入队（退避未到），
        // 清扫不应动它，摘要应数到它。
        var queued = Path.Combine(BasePath, "queued.bin");
        File.WriteAllText(queued, "queued");
        db.PendingFileDeletions.Add(new PendingFileDeletion
        {
            StoragePath = "queued.bin",
            Reason = "test",
        });

        await db.SaveChangesAsync();
        await MessageSearchFts.EnsureAsync(db, NullLogger.Instance);
    }

    private void WriteLogFile(DateOnly utcDate, params (DateTimeOffset Timestamp, string Message)[] lines)
    {
        Directory.CreateDirectory(LogDir);
        var path = Path.Combine(LogDir, $"anydrop-{utcDate:yyyyMMdd}.log");
        File.WriteAllText(path, string.Join("\n", lines.Select(l =>
            $"{l.Timestamp:O} [Warning] AnyDrop.Services.ShareService: {l.Message}")));
    }

    [Fact]
    public async Task BuildSummaryAsync_CountsMatchSeededState()
    {
        var factory = CreateFactory();
        await SeedAsync(factory);

        // 摘要日 = 今天（本机时区；测试容器里是 UTC）。窗口内各写一条失败标记，
        // 窗口外再写一条缩略图标记（不应被数到）。
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var inWindow = new DateTimeOffset(today.Year, today.Month, today.Day, 10, 0, 0, TimeSpan.Zero);
        var yesterday = today.AddDays(-1);
        var outOfWindow = new DateTimeOffset(yesterday.Year, yesterday.Month, yesterday.Day, 23, 0, 0, TimeSpan.Zero);
        WriteLogFile(today,
            (inWindow, "Background thumbnail generation failed for item 11111111-1111-1111-1111-111111111111"),
            (inWindow.AddHours(2), "Expired message cleanup cycle failed."),
            (outOfWindow, "Background thumbnail generation failed for item 22222222-2222-2222-2222-222222222222"));

        var sut = CreateSut(factory);
        var summary = await sut.BuildSummaryAsync(today, CancellationToken.None);

        summary.FilesScanned.Should().Be(3);
        summary.OrphansFound.Should().Be(1);
        summary.MissingReferencedFiles.Should().Be(0);
        summary.MissingReportsOpen.Should().Be(0);
        summary.PendingDeleteQueue.Should().Be(1);
        summary.ThumbnailFailures.Should().Be(1);
        summary.CleanupFailures.Should().Be(1);
        summary.FtsTotal.Should().Be(1);
        summary.FtsIndexed.Should().Be(1);
    }

    [Fact]
    public async Task BuildSummaryAsync_WhenLogFilesMissing_FailureCountsAreNull()
    {
        var factory = CreateFactory();
        await SeedAsync(factory);

        var sut = CreateSut(factory);
        var summary = await sut.BuildSummaryAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        summary.ThumbnailFailures.Should().BeNull();
        summary.CleanupFailures.Should().BeNull();
        summary.OrphansFound.Should().Be(1);
    }

    [Fact]
    public void RenderMarkdown_ContainsCountsAndNaForNulls()
    {
        var summary = new DailySummary(
            new DateOnly(2026, 11, 14),
            FilesScanned: 10,
            OrphansFound: 2,
            OrphanBytes: 12345,
            MissingReferencedFiles: 1,
            MissingReportsOpen: 1,
            PendingDeleteQueue: 3,
            SweepRowsRemoved: 0,
            SweepFilesDeleted: 0,
            QueueEntriesDrained: 0,
            QueueEntriesFailed: 0,
            StalePlaceholdersRemoved: 0,
            ThumbnailFailures: null,
            CleanupFailures: 0,
            FtsTotal: 5,
            FtsIndexed: 5);

        var text = DailySummaryService.RenderMarkdown(summary);

        text.Should().Contain("# 每日运维摘要 2026-11-14");
        text.Should().Contain("| 孤儿文件 | 2（12345 字节） |");
        text.Should().Contain("| 待删除队列 | 3 |");
        text.Should().Contain("| 缩略图失败 | n/a |");
        text.Should().Contain("| FTS 索引一致性（消息行 / 已索引） | 5 / 5 |");
    }

    [Fact]
    public async Task WriteSummaryAsync_WritesFileAndPrunesOldSummaries()
    {
        var factory = CreateFactory();
        var sut = CreateSut(factory);
        var summariesDir = Path.Combine(LogDir, "summaries");
        Directory.CreateDirectory(summariesDir);

        var oldSummary = Path.Combine(summariesDir, "daily-summary-2026-01-01.md");
        File.WriteAllText(oldSummary, "old");

        var summary = new DailySummary(
            new DateOnly(2026, 11, 14),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, null);
        await sut.WriteSummaryAsync(summary, CancellationToken.None);

        File.ReadAllText(Path.Combine(summariesDir, "daily-summary-2026-11-14.md"))
            .Should().Contain("# 每日运维摘要 2026-11-14");
        // 2026-01-01 距今远超 90 天，应被清理。
        File.Exists(oldSummary).Should().BeFalse();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响测试结果。
        }
    }
}
