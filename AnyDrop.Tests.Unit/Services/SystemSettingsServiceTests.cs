using AnyDrop.Data;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.Services;

public class SystemSettingsServiceTests
{
    [Fact]
    public async Task GetSecuritySettingsAsync_WhenEmpty_ShouldCreateDefault()
    {
        await using var db = CreateDbContext();
        var sut = new SystemSettingsService(db);

        var result = await sut.GetSecuritySettingsAsync();

        result.AutoFetchLinkPreview.Should().BeTrue();
        (await db.SystemSettings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateSecuritySettingsAsync_ShouldPersist()
    {
        await using var db = CreateDbContext();
        var sut = new SystemSettingsService(db);

        var request = new AnyDrop.Models.UpdateSecuritySettingsRequest(false, "UTC", 10, "zh-CN");
        var result = await sut.UpdateSecuritySettingsAsync(request);

        result.Succeeded.Should().BeTrue();
        (await sut.IsAutoFetchLinkPreviewEnabledAsync()).Should().BeFalse();
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-settings-{Guid.NewGuid():N}")
            .Options;
        return new AnyDropDbContext(options);
    }
}
