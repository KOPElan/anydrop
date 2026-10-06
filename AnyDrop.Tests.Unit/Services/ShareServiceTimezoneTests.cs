using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// 日期维度查询的时区正确性。
///
/// 此前实现用 <c>DateTimeKind.Local</c> 拼 <see cref="DateTimeOffset"/>，取的是**服务器**
/// 时区；而界面按**浏览器**时区把消息归入具体日期。两者不一致时，日期搜索结果会整体错位。
///
/// 这里用 <see cref="TimeZoneInfo.CreateCustomTimeZone"/> 自造时区，不依赖运行环境的
/// 时区数据库，保证在 Windows / Linux 上结果一致。
/// </summary>
public class ShareServiceTimezoneTests
{
    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-tz-{Guid.NewGuid():N}")
            .Options;

        return new AnyDropDbContext(options);
    }

    private static TimeZoneInfo FixedOffset(string id, TimeSpan offset)
        => TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);

    /// <summary>带夏令时的时区：3 月第 2 个周日 02:00 开始，11 月第 1 个周日 02:00 结束（-5 → -4）。</summary>
    private static TimeZoneInfo CreateDstZone()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, dayOfWeek: DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, dayOfWeek: DayOfWeek.Sunday);

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test-DST", TimeSpan.FromHours(-5), "Test-DST", "Test-STD", "Test-DST", [rule]);
    }

    private static async Task<Guid> SeedAsync(AnyDropDbContext db, params DateTimeOffset[] timestamps)
    {
        var topicId = Guid.NewGuid();
        db.Topics.Add(new Topic { Id = topicId, Name = "tz", CreatedAt = DateTimeOffset.UtcNow });

        foreach (var createdAt in timestamps)
        {
            db.ShareItems.Add(new ShareItem
            {
                Id = Guid.NewGuid(),
                TopicId = topicId,
                ContentType = ShareContentType.Text,
                Content = $"msg-{createdAt.UtcTicks}",
                CreatedAt = createdAt
            });
        }

        await db.SaveChangesAsync();
        return topicId;
    }

    // ── GetUtcRange ───────────────────────────────────────────────────────────

    [Fact]
    public void GetUtcRange_ForNonUtcZone_ShiftsBoundariesByOffset()
    {
        var zone = FixedOffset("Test-Plus8", TimeSpan.FromHours(8));

        var (startUtc, endUtc) = ShareService.GetUtcRange(
            new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2), zone);

        // 本地 2026-01-02 00:00 (+08:00) == 2026-01-01 16:00Z
        startUtc.Should().Be(new DateTimeOffset(2026, 1, 1, 16, 0, 0, TimeSpan.Zero));
        endUtc.Should().Be(new DateTimeOffset(2026, 1, 2, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetUtcRange_AcrossDstTransition_IsNotAlwaysTwentyFourHours()
    {
        var zone = CreateDstZone();

        // 2026-03-08 是美国夏令时开始日，这一天只有 23 小时
        var (startUtc, endUtc) = ShareService.GetUtcRange(
            new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 8), zone);

        (endUtc - startUtc).TotalHours.Should().Be(23);
    }

    [Fact]
    public void GetUtcRange_AcrossMultipleDays_CoversWholeSpan()
    {
        var zone = FixedOffset("Test-Minus5", TimeSpan.FromHours(-5));

        var (startUtc, endUtc) = ShareService.GetUtcRange(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), zone);

        (endUtc - startUtc).TotalDays.Should().Be(3);
    }

    // ── 按日期查消息 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopicMessagesByDateAsync_AttributesMessageToDateInCallerTimezone()
    {
        // 2026-01-01T23:30Z：在 UTC 属于 1 月 1 日，在东八区已经是 1 月 2 日
        var instant = new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero);

        await using var db = CreateDbContext();
        var topicId = await SeedAsync(db, instant);
        var service = ShareServiceFactory.Create(db);

        var plus8 = FixedOffset("Test-Plus8", TimeSpan.FromHours(8));
        var utc = TimeZoneInfo.Utc;

        var inPlus8 = await service.GetTopicMessagesByDateAsync(topicId, new DateOnly(2026, 1, 2), plus8);
        var inUtc = await service.GetTopicMessagesByDateAsync(topicId, new DateOnly(2026, 1, 1), utc);
        var wrongDay = await service.GetTopicMessagesByDateAsync(topicId, new DateOnly(2026, 1, 1), plus8);

        inPlus8.Should().ContainSingle("东八区此刻已是 1 月 2 日");
        inUtc.Should().ContainSingle("UTC 下仍是 1 月 1 日");
        wrongDay.Should().BeEmpty();
    }

    // ── 活跃日期 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopicActiveDatesAsync_GroupsByCallerTimezone()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.Zero);

        await using var db = CreateDbContext();
        var topicId = await SeedAsync(db, instant);
        var service = ShareServiceFactory.Create(db);

        var plus8 = FixedOffset("Test-Plus8", TimeSpan.FromHours(8));

        var utcDates = await service.GetTopicActiveDatesAsync(
            topicId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), TimeZoneInfo.Utc);
        var plus8Dates = await service.GetTopicActiveDatesAsync(
            topicId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), plus8);

        utcDates.Should().Equal(new DateOnly(2026, 1, 1));
        plus8Dates.Should().Equal(new DateOnly(2026, 1, 2));
    }

    [Fact]
    public async Task GetTopicActiveDatesAsync_AcrossMidnightInSameZone_ReturnsBothDays()
    {
        var beforeMidnight = new DateTimeOffset(2026, 1, 1, 15, 0, 0, TimeSpan.Zero); // 东八区 1/1 23:00
        var afterMidnight = new DateTimeOffset(2026, 1, 1, 16, 0, 0, TimeSpan.Zero);  // 东八区 1/2 00:00

        await using var db = CreateDbContext();
        var topicId = await SeedAsync(db, beforeMidnight, afterMidnight);
        var service = ShareServiceFactory.Create(db);

        var plus8 = FixedOffset("Test-Plus8", TimeSpan.FromHours(8));
        var dates = await service.GetTopicActiveDatesAsync(
            topicId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), plus8);

        dates.Should().BeEquivalentTo([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)]);
    }

    // ── 端点侧的时区解析 ──────────────────────────────────────────────────────

    [Fact]
    public void TryResolveTimeZone_WithUtc_Resolves()
    {
        AnyDrop.Api.TopicEndpoints.TryResolveTimeZone("UTC", out var zone, out var error).Should().BeTrue();
        zone.Should().Be(TimeZoneInfo.Utc);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryResolveTimeZone_WhenMissing_DefaultsToUtc()
    {
        AnyDrop.Api.TopicEndpoints.TryResolveTimeZone(null, out var zone, out _).Should().BeTrue();
        zone.Should().Be(TimeZoneInfo.Utc);

        AnyDrop.Api.TopicEndpoints.TryResolveTimeZone("   ", out var blank, out _).Should().BeTrue();
        blank.Should().Be(TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData("Not/A-Real-Zone")]
    [InlineData("Mars/Olympus")]
    public void TryResolveTimeZone_WithUnknownId_FailsWithMessage(string id)
    {
        AnyDrop.Api.TopicEndpoints.TryResolveTimeZone(id, out var zone, out var error).Should().BeFalse();
        zone.Should().Be(TimeZoneInfo.Utc, "解析失败时回退到 UTC，不抛异常");
        error.Should().Contain(id);
    }

    [Fact]
    public void TryResolveTimeZone_WithIanaId_ResolvesOnThisPlatform()
    {
        // .NET 6+ 在 Windows 上也支持 IANA 标识；这里显式验证，避免我们依赖一个
        // 只在实际部署平台（Linux）成立的假设。
        var ok = AnyDrop.Api.TopicEndpoints.TryResolveTimeZone("Asia/Shanghai", out var zone, out _);

        ok.Should().BeTrue();
        zone.GetUtcOffset(new DateTime(2026, 1, 1)).Should().Be(TimeSpan.FromHours(8));
    }
}
