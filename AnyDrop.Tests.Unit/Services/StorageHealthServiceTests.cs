using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S1-01 缺失引用修复入口（StorageHealthService）的测试。
/// </summary>
public class StorageHealthServiceTests
{
    private static TestDbContextFactory CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-storage-health-{Guid.NewGuid():N}")
            .Options;

        return new TestDbContextFactory(options);
    }

    private static StorageHealthService CreateSut(TestDbContextFactory factory)
    {
        var shareService = ShareServiceFactory.Create(factory);
        return new StorageHealthService(factory, shareService, TimeProvider.System);
    }

    private static async Task SeedReportAsync(
        TestDbContextFactory factory, string fileName, string topicName = "general")
    {
        await using (var db = factory.CreateDbContext())
        {
            var topic = new Topic { Name = topicName };
            db.Topics.Add(topic);
            var item = new ShareItem
            {
                ContentType = ShareContentType.File,
                Content = $"20260101/{fileName}",
                FileName = fileName,
                TopicId = topic.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ShareItems.Add(item);
            await db.SaveChangesAsync();

            db.MissingFileReports.Add(new MissingFileReport
            {
                ShareItemId = item.Id,
                StoragePath = $"20260101/{fileName}",
                Kind = "content",
                FirstDetectedAt = DateTimeOffset.UtcNow.AddHours(-2),
                LastSeenAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ListMissingFileReportsAsync_ReturnsOpenWithItemInfo()
    {
        var factory = CreateDbContextFactory();
        await SeedReportAsync(factory, "photo.png");

        var sut = CreateSut(factory);
        var reports = await sut.ListMissingFileReportsAsync();

        reports.Should().HaveCount(1);
        reports[0].StoragePath.Should().Be("20260101/photo.png");
        reports[0].FileName.Should().Be("photo.png");
        reports[0].TopicName.Should().Be("general");
        reports[0].Kind.Should().Be("content");
    }

    [Fact]
    public async Task ListMissingFileReportsAsync_HidesResolved()
    {
        var factory = CreateDbContextFactory();
        await SeedReportAsync(factory, "photo.png");
        await using (var db = factory.CreateDbContext())
        {
            var report = await db.MissingFileReports.SingleAsync();
            report.IsResolved = true;
            await db.SaveChangesAsync();
        }

        var sut = CreateSut(factory);
        var reports = await sut.ListMissingFileReportsAsync();

        reports.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveMissingFileAsync_DeleteMessage_DeletesItemAndResolves()
    {
        var factory = CreateDbContextFactory();
        await SeedReportAsync(factory, "gone.png");

        Guid reportId;
        await using (var db = factory.CreateDbContext())
        {
            reportId = (await db.MissingFileReports.SingleAsync()).Id;
        }

        var sut = CreateSut(factory);
        var resolved = await sut.ResolveMissingFileAsync(reportId, "delete-message");

        resolved.Should().BeTrue();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        var report = await verifyDb.MissingFileReports.SingleAsync();
        report.IsResolved.Should().BeTrue();
        report.ResolvedBy.Should().Be("delete-message");
    }

    [Fact]
    public async Task ResolveMissingFileAsync_Dismiss_KeepsItem()
    {
        var factory = CreateDbContextFactory();
        await SeedReportAsync(factory, "keep.png");

        Guid reportId;
        await using (var db = factory.CreateDbContext())
        {
            reportId = (await db.MissingFileReports.SingleAsync()).Id;
        }

        var sut = CreateSut(factory);
        var resolved = await sut.ResolveMissingFileAsync(reportId, "dismiss");

        resolved.Should().BeTrue();
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        var report = await verifyDb.MissingFileReports.SingleAsync();
        report.IsResolved.Should().BeTrue();
        report.ResolvedBy.Should().Be("dismiss");
    }

    [Fact]
    public async Task ResolveMissingFileAsync_NotFound_ReturnsFalse()
    {
        var factory = CreateDbContextFactory();
        var sut = CreateSut(factory);

        var resolved = await sut.ResolveMissingFileAsync(Guid.NewGuid(), "dismiss");

        resolved.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveMissingFileAsync_InvalidAction_Throws()
    {
        var factory = CreateDbContextFactory();
        await SeedReportAsync(factory, "x.png");
        Guid reportId;
        await using (var db = factory.CreateDbContext())
        {
            reportId = (await db.MissingFileReports.SingleAsync()).Id;
        }

        var sut = CreateSut(factory);
        var act = () => sut.ResolveMissingFileAsync(reportId, "explode");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
