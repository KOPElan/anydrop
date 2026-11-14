using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S1-03（DAT-09）回归组：DbContextFactory + 短寿命上下文 + 分批游标。
/// </summary>
public class DbContextFactoryTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(AppContext.BaseDirectory, "test-factory", $"{Guid.NewGuid():N}.db");

    private TestDbContextFactory CreateSqliteFactory()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(new SqliteBusyTimeoutInterceptor())
            .Options;
        return new TestDbContextFactory(options);
    }

    /// <summary>
    /// 设置单例「先查后插」遇主键冲突时重读返回，不抛 500。
    ///
    /// 触发方式：先删掉种子行，再让 20 个调用并发走「查不到→插入」；
    /// 只有一个能插成功，其余撞主键冲突后必须重读到已提交的那行。
    /// 用真实 SQLite（生产 provider）：InMemory 不按同样方式强制主键唯一。
    /// </summary>
    [Fact]
    public async Task EnsureSettingsAsync_ConcurrentFirstCalls_AllSucceedWithSingleRow()
    {
        var factory = CreateSqliteFactory();
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            // 删掉 HasData 种子行，制造「查不到」的竞态窗口
            await setup.Database.ExecuteSqlRawAsync("DELETE FROM SystemSettings;");
        }

        var sut = new SystemSettingsService(factory);
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => sut.GetSecuritySettingsAsync())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(20);
        results.Should().OnlyContain(r => r.AutoFetchLinkPreview);
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.SystemSettings.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// 长电路持有同一上下文的旧病：服务实例复用时，后一次写操作
    /// 不能用上一次读到的旧实体覆盖外部已改的字段。
    /// 旧实现里 TopicService 持有请求/电路作用域的上下文，Pin 后外部改名、
    /// 再 Archive 时会把名字写回旧值；工厂版每次调用都重新加载。
    /// </summary>
    [Fact]
    public async Task TopicService_ReuseAfterExternalChange_DoesNotOverwriteExternalEdit()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-factory-stale-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);

        Guid topicId;
        await using (var seed = factory.CreateDbContext())
        {
            var topic = new Topic { Name = "Original" };
            seed.Topics.Add(topic);
            await seed.SaveChangesAsync();
            topicId = topic.Id;
        }

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(new Mock<IClientProxy>().Object);
        var hubContext = new Mock<IHubContext<ShareHub>>();
        hubContext.Setup(c => c.Clients).Returns(hubClients.Object);
        var sut = new TopicService(factory, hubContext.Object, TimeProvider.System);

        await sut.PinTopicAsync(topicId, true);

        // 外部改动：另一路把名字改掉
        await using (var external = factory.CreateDbContext())
        {
            var topic = await external.Topics.SingleAsync(x => x.Id == topicId);
            topic.Name = "Renamed";
            await external.SaveChangesAsync();
        }

        await sut.ArchiveTopicAsync(topicId, true);

        await using var verifyDb = factory.CreateDbContext();
        var entity = await verifyDb.Topics.SingleAsync(x => x.Id == topicId);
        entity.Name.Should().Be("Renamed", "后一次写操作应基于最新数据，不能覆盖外部改动");
        entity.IsArchived.Should().BeTrue();
        entity.IsPinned.Should().BeFalse("归档时自动取消置顶");
    }

    /// <summary>
    /// 活跃日期分批游标：1200 条消息跨两天（每批 1000 条），两页都不能漏。
    /// </summary>
    [Fact]
    public async Task GetTopicActiveDatesAsync_OverBatchSize_ReturnsAllDates()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-factory-dates-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);

        var topicId = Guid.NewGuid();
        await using (var seed = factory.CreateDbContext())
        {
            seed.Topics.Add(new Topic { Id = topicId, Name = "t", CreatedAt = DateTimeOffset.UtcNow });
            var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 1200; i++)
            {
                seed.ShareItems.Add(new ShareItem
                {
                    TopicId = topicId,
                    ContentType = ShareContentType.Text,
                    Content = $"m{i}",
                    CreatedAt = baseTime.AddMinutes(i % 2 == 0 ? 0 : 60 * 24)
                });
            }

            await seed.SaveChangesAsync();
        }

        var sut = ShareServiceFactory.Create(factory);
        var dates = await sut.GetTopicActiveDatesAsync(
            topicId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), TimeZoneInfo.Utc);

        dates.Should().BeEquivalentTo([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)]);
    }

    /// <summary>
    /// 万级数据分批游标（S1-03 验收规模）：10000 条消息跨 10 天，每批 1000 条，
    /// 10 个批次都不能漏、不能重复。
    /// </summary>
    [Fact]
    public async Task GetTopicActiveDatesAsync_TenThousandMessages_ReturnsAllDates()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-factory-dates-10k-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);

        var topicId = Guid.NewGuid();
        await using (var seed = factory.CreateDbContext())
        {
            seed.Topics.Add(new Topic { Id = topicId, Name = "t", CreatedAt = DateTimeOffset.UtcNow });
            var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 10000; i++)
            {
                seed.ShareItems.Add(new ShareItem
                {
                    TopicId = topicId,
                    ContentType = ShareContentType.Text,
                    Content = $"m{i}",
                    CreatedAt = baseTime.AddDays(i / 1000)
                });
            }

            await seed.SaveChangesAsync();
        }

        var sut = ShareServiceFactory.Create(factory);
        var dates = await sut.GetTopicActiveDatesAsync(
            topicId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10), TimeZoneInfo.Utc);

        dates.Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(d => new DateOnly(2026, 1, d)));
    }

    /// <summary>
    /// 批量清理分批标记：600 条超龄消息（每批 500 条），两批都要标记到位。
    /// </summary>
    [Fact]
    public async Task CleanupOldMessagesAsync_OverBatchSize_MarksAll()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-factory-cleanup-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        await using (var seed = factory.CreateDbContext())
        {
            var old = clock.GetUtcNow().AddMonths(-2);
            for (var i = 0; i < 600; i++)
            {
                seed.ShareItems.Add(new ShareItem
                {
                    ContentType = ShareContentType.Text,
                    Content = $"old-{i}",
                    CreatedAt = old
                });
            }

            await seed.SaveChangesAsync();
        }

        var sut = ShareServiceFactory.Create(factory, timeProvider: clock);
        var marked = await sut.CleanupOldMessagesAsync(1);

        marked.Should().Be(600);
        await using var verifyDb = factory.CreateDbContext();
        var hidden = await verifyDb.ShareItems.IgnoreQueryFilters().ToListAsync();
        hidden.Should().HaveCount(600);
        hidden.Should().OnlyContain(x => x.DeleteAfter == clock.GetUtcNow().AddDays(DeletionPolicy.DelayedDeleteDays));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        var directory = Path.GetDirectoryName(_dbPath);
        if (directory is not null
            && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }
}
