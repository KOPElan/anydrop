using System.Diagnostics;
using System.Globalization;
using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AnyDrop.Tests.Unit.Data;

/// <summary>
/// S3-03：FTS5 搜索索引。全部用真实 SQLite 文件验证（不是 InMemory）：
/// 建表、触发器同步、存量回填、一致性校验、中文查询语义、翻页与可见性过滤，
/// 以及万级数据的 FTS vs LIKE 耗时对比（数字见 docs/search-fts5.md）。
/// </summary>
public class MessageSearchFtsTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(AppContext.BaseDirectory, "test-fts", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task EnsureAsync_SeedsBeforeEnsure_AreBackfilledAndSearchable()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        var seeded = AddMessage(db, topicId, "你好世界，欢迎使用 AnyDrop");
        await db.SaveChangesAsync();

        var available = await MessageSearchFts.EnsureAsync(db, NullLogger.Instance);
        available.Should().BeTrue();

        var (total, indexed) = await MessageSearchFts.GetConsistencyAsync(db);
        total.Should().Be(1);
        indexed.Should().Be(1);

        var service = CreateServiceWithFts();
        var result = await service.SearchTopicMessagesAsync(topicId, "你好");
        result.Messages.Select(x => x.Id).Should().Contain(seeded);
    }

    [Fact]
    public async Task EnsureAsync_IsIdempotent_NoDuplicateIndexRows()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        AddMessage(db, topicId, "hello world");
        await db.SaveChangesAsync();

        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        var (total, indexed) = await MessageSearchFts.GetConsistencyAsync(db);
        total.Should().Be(1);
        indexed.Should().Be(1);
    }

    [Fact]
    public async Task Triggers_InsertUpdateDelete_KeepIndexInSync()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();
        var service = CreateServiceWithFts();

        // 新增行随写随索引
        var id = AddMessage(db, topicId, "冰霜龙的传说");
        await db.SaveChangesAsync();
        (await service.SearchTopicMessagesAsync(topicId, "冰霜")).Messages
            .Select(x => x.Id).Should().Contain(id);

        // 改内容：旧词消失、新词出现
        var item = await db.ShareItems.SingleAsync(x => x.Id == id);
        item.Content = "烈焰凤凰的传说";
        await db.SaveChangesAsync();
        (await service.SearchTopicMessagesAsync(topicId, "冰霜")).Messages.Should().BeEmpty();
        (await service.SearchTopicMessagesAsync(topicId, "烈焰")).Messages
            .Select(x => x.Id).Should().Contain(id);

        // 删行：索引同步消失
        db.ShareItems.Remove(item);
        await db.SaveChangesAsync();
        (await service.SearchTopicMessagesAsync(topicId, "烈焰")).Messages.Should().BeEmpty();
        var (total, indexed) = await MessageSearchFts.GetConsistencyAsync(db);
        total.Should().Be(0);
        indexed.Should().Be(0);
    }

    [Fact]
    public async Task Search_CjkPrefixMatch_FoundViaFts()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        var id = AddMessage(db, topicId, "你好世界");
        await db.SaveChangesAsync();
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        var service = CreateServiceWithFts();
        (await service.SearchTopicMessagesAsync(topicId, "你好")).Messages
            .Select(x => x.Id).Should().Contain(id);
        // 英文大小写不敏感（unicode61 自带折叠，与旧 LIKE 行为一致）
        var english = AddMessage(db, topicId, "Hello World");
        await db.SaveChangesAsync();
        (await service.SearchTopicMessagesAsync(topicId, "hello")).Messages
            .Select(x => x.Id).Should().Contain(english);
    }

    [Fact]
    public async Task Search_CjkMidTokenSubstring_FallsBackToLike()
    {
        // unicode61 把连续中文切成一个词，“好世”命中不了“你好世界”；
        // FTS 结果为空时 LIKE 兜底必须把它找回来。
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        var id = AddMessage(db, topicId, "你好世界");
        var dragon = AddMessage(db, topicId, "冰霜龙的传说");
        await db.SaveChangesAsync();
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        var service = CreateServiceWithFts();
        (await service.SearchTopicMessagesAsync(topicId, "好世")).Messages
            .Select(x => x.Id).Should().Contain(id);
        (await service.SearchTopicMessagesAsync(topicId, "龙")).Messages
            .Select(x => x.Id).Should().Contain(dragon);
    }

    [Fact]
    public async Task Search_FtsUnavailable_FallsBackToLike()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        var id = AddMessage(db, topicId, "你好世界");
        await db.SaveChangesAsync();
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        // 索引明明可用，但标记为不可用时应全程走 LIKE，结果一致。
        var service = ShareServiceFactory.Create(CreateSqliteFactory(), searchIndex: new MessageSearchIndex());
        (await service.SearchTopicMessagesAsync(topicId, "好世")).Messages
            .Select(x => x.Id).Should().Contain(id);
    }

    [Fact]
    public async Task Search_BeforeCursor_PaginatesInCreatedAtOrder()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(AddMessage(db, topicId, $"searchable message {i}",
                createdAt: now.AddMinutes(-i)));
        }
        await db.SaveChangesAsync();
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        var service = CreateServiceWithFts();
        var page1 = await service.SearchTopicMessagesAsync(topicId, "searchable", limit: 2);
        page1.Messages.Select(x => x.Id).Should().Equal(ids[0], ids[1]);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();

        var before = DateTimeOffset.Parse(page1.NextCursor!);
        var page2 = await service.SearchTopicMessagesAsync(topicId, "searchable", limit: 2, before: before);
        page2.Messages.Select(x => x.Id).Should().Equal(ids[2]);
        page2.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Search_InvisibleMessages_NotReturnedViaFts()
    {
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        var now = DateTimeOffset.UtcNow;
        AddMessage(db, topicId, "visible searchable");
        AddMessage(db, topicId, "expired searchable", expiresAt: now.AddMinutes(-1));
        AddMessage(db, topicId, "deleted searchable", deleteAfter: now.AddDays(1));
        var uploading = AddMessage(db, topicId, "uploading searchable");
        await db.SaveChangesAsync();
        // Uploading 行被全局查询过滤器隐藏，须绕过过滤器才能改它。
        var uploadingItem = await db.ShareItems.IgnoreQueryFilters().SingleAsync(x => x.Id == uploading);
        uploadingItem.UploadState = UploadState.Uploading;
        await db.SaveChangesAsync();
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        var service = CreateServiceWithFts();
        var result = await service.SearchTopicMessagesAsync(topicId, "searchable");
        result.Messages.Should().HaveCount(1);
        result.Messages[0].Content.Should().Be("visible searchable");
    }

    [Theory]
    [InlineData("C++", "\"C\"*")]
    [InlineData("Quarterly-Report", "\"Quarterly\"* \"Report\"*")]
    [InlineData("hello world", "\"hello\"* \"world\"*")]
    [InlineData("你好 世界", "\"你好\"* \"世界\"*")]
    public void BuildMatchQuery_TokenizesAndPrefixMatches(string query, string expected)
        => MessageSearchFts.BuildMatchQuery(query).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("---")]
    public void BuildMatchQuery_NoTerms_ReturnsNull(string query)
        => MessageSearchFts.BuildMatchQuery(query).Should().BeNull();

    [Fact]
    public async Task SearchIdsAsync_ReturnsFtsMatchesDirectly()
    {
        // 直接测原生 FTS 查询（不经过 ShareService 的 LIKE 兜底）：
        // 若 FTS 路径本身是坏的，这个测试会失败，而上层搜索测试会因
        // LIKE 兜底悄悄通过、掩盖问题。
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        var id = AddMessage(db, topicId, "你好世界");
        AddMessage(db, topicId, "无关消息");
        await db.SaveChangesAsync();
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        var ids = await MessageSearchFts.SearchIdsAsync(
            db, topicId, MessageSearchFts.BuildMatchQuery("你好")!, 10, null, DateTimeOffset.UtcNow);

        ids.Should().Equal(id);
    }

    [Fact]
    public async Task SearchIdsAsync_BeforeCursor_ExcludesNewerRows()
    {
        // before 比较走 epoch 秒：同日内的先后不能被文本格式带偏。
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        var now = DateTimeOffset.UtcNow;
        var newer = AddMessage(db, topicId, "searchable newer", createdAt: now.AddMinutes(-1));
        var older = AddMessage(db, topicId, "searchable older", createdAt: now.AddMinutes(-2));
        await db.SaveChangesAsync();
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        var before = now.AddMinutes(-1).AddSeconds(-30);
        var ids = await MessageSearchFts.SearchIdsAsync(
            db, topicId, MessageSearchFts.BuildMatchQuery("searchable")!, 10, before, now);

        ids.Should().Equal(older);
        ids.Should().NotContain(newer);
    }

    [Fact]
    public async Task Benchmark_10kRows_FtsVsLike()
    {
        // 万级数据耗时对比（留档用，见 docs/search-fts5.md）。
        // 只断言正确性（FTS 与 LIKE 对词首查询返回同一批 Id），不做耗时断言——
        // CI 机器负载波动会让耗时断言不稳定。
        await using var db = CreateSqliteContext();
        var topicId = await SeedTopicAsync(db);
        (await MessageSearchFts.EnsureAsync(db, NullLogger.Instance)).Should().BeTrue();

        await SeedBulkAsync(db, topicId, 10_000);
        var (total, indexed) = await MessageSearchFts.GetConsistencyAsync(db);
        // 10_000 批量行 + 1 行 rare 查询专用行。
        total.Should().Be(10_001);
        indexed.Should().Be(10_001);

        var ftsService = CreateServiceWithFts();
        var likeService = ShareServiceFactory.Create(CreateSqliteFactory(), searchIndex: new MessageSearchIndex());

        const string commonQuery = "report";
        const string rareQuery = "zebra-7741";
        const string cjkQuery = "冰霜";

        var ftsCommon = await ftsService.SearchTopicMessagesAsync(topicId, commonQuery, limit: 100);
        var likeCommon = await likeService.SearchTopicMessagesAsync(topicId, commonQuery, limit: 100);
        ftsCommon.Messages.Select(x => x.Id).Should()
            .BeEquivalentTo(likeCommon.Messages.Select(x => x.Id));

        var timings = new List<string>();
        foreach (var (label, query) in new[] { ("common", commonQuery), ("rare", rareQuery), ("cjk", cjkQuery) })
        {
            var ftsMs = await TimeSearchAsync(ftsService, topicId, query);
            var likeMs = await TimeSearchAsync(likeService, topicId, query);
            timings.Add($"{label}: FTS {ftsMs:F1}ms vs LIKE {likeMs:F1}ms (x{likeMs / Math.Max(ftsMs, 0.1):F1})");
        }

        output.WriteLine($"10k rows, limit 100, 20 iterations each (this VM, {DateTimeOffset.UtcNow:yyyy-MM-dd}):");
        foreach (var line in timings)
        {
            output.WriteLine("  " + line);
        }
    }

    private static async Task<double> TimeSearchAsync(ShareService service, Guid topicId, string query)
    {
        // 热身一次（JIT、连接池、缓存页），再测 20 次取平均。
        await service.SearchTopicMessagesAsync(topicId, query, limit: 100);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            await service.SearchTopicMessagesAsync(topicId, query, limit: 100);
        }
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds / 20;
    }

    private async Task SeedBulkAsync(AnyDropDbContext db, Guid topicId, int count)
    {
        // 原生 SQL 批量插（EF AddRange 逐行插 1 万行太慢）；触发器随写随更索引。
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var tx = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)tx;
        command.CommandText =
            "INSERT INTO \"ShareItems\" (\"Id\", \"ContentType\", \"Content\", \"FileName\", \"CreatedAt\", \"TopicId\", \"UploadState\") " +
            "VALUES ($id, 0, $content, $fileName, $createdAt, $topic, 1);";
        var idParam = new SqliteParameter("$id", string.Empty);
        var contentParam = new SqliteParameter("$content", string.Empty);
        var fileParam = new SqliteParameter("$fileName", string.Empty);
        var createdParam = new SqliteParameter("$createdAt", DateTimeOffset.UtcNow);
        var topicParam = new SqliteParameter("$topic", topicId.ToString("D").ToUpperInvariant());
        command.Parameters.AddRange([idParam, contentParam, fileParam, createdParam, topicParam]);

        var now = DateTimeOffset.UtcNow;
        var random = new Random(42);
        for (var i = 0; i < count; i++)
        {
            // Guid 与时间都按 EF 的存储格式写：Guid 大写 TEXT、时间 UTC 的
            // yyyy-MM-dd HH:mm:ss 文本（见 MessageSearchFts.SearchIdsAsync 的
            // 格式陷阱说明）。原生参数若直接传 Guid / DateTimeOffset 对象，
            // 存成的格式与 EF 不一致，比较与排序都会错。
            idParam.Value = Guid.NewGuid().ToString("D").ToUpperInvariant();
            var isReport = i % 10 == 0;
            var isCjk = i % 25 == 0;
            contentParam.Value = isReport
                ? $"Quarterly report number {i} about sales and marketing"
                : isCjk
                    ? $"冰霜龙的传说第 {i} 章，北风吹过雪原"
                    : $"Random note {i} about topic {random.Next(1000)} and daily work";
            fileParam.Value = isReport ? (object)$"Quarterly-Report-{i}.pdf" : DBNull.Value;
            createdParam.Value = now.AddMinutes(-i).UtcDateTime
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            await command.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        // rare 查询用的唯一行：内容含固定词 zebra-7741，确保 rare 查询命中恰好 1 行。
        db.ShareItems.Add(new ShareItem
        {
            Id = Guid.NewGuid(),
            TopicId = topicId,
            ContentType = ShareContentType.Text,
            Content = "The zebra-7741 is a rare specimen note",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private AnyDropDbContext CreateSqliteContext()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        var db = new AnyDropDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private TestDbContextFactory CreateSqliteFactory()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private ShareService CreateServiceWithFts()
        => ShareServiceFactory.Create(CreateSqliteFactory(), searchIndex: new MessageSearchIndex { IsAvailable = true });

    private static async Task<Guid> SeedTopicAsync(AnyDropDbContext db)
    {
        var topicId = Guid.NewGuid();
        db.Topics.Add(new Topic { Id = topicId, Name = "topic", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return topicId;
    }

    private static Guid AddMessage(
        AnyDropDbContext db,
        Guid topicId,
        string content,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? deleteAfter = null)
    {
        var id = Guid.NewGuid();
        db.ShareItems.Add(new ShareItem
        {
            Id = id,
            TopicId = topicId,
            ContentType = ShareContentType.Text,
            Content = content,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            DeleteAfter = deleteAfter
        });
        return id;
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // 测试进程退出时 SQLite 可能尚未释放文件句柄，忽略清理失败。
        }
    }
}
