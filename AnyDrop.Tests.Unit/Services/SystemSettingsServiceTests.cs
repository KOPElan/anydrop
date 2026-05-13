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

        var request = new UpdateSecuritySettingsRequest(false, 10, "zh-CN", false, 1);
        var result = await sut.UpdateSecuritySettingsAsync(request);

        result.Succeeded.Should().BeTrue();
        (await sut.IsAutoFetchLinkPreviewEnabledAsync()).Should().BeFalse();
    }

    // ── ThumbnailGenerationHour 校验 ──────────────────────────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(100)]
    public async Task UpdateSecuritySettingsAsync_WithInvalidThumbnailHour_ShouldFail(int invalidHour)
    {
        await using var db = CreateDbContext();
        var sut = new SystemSettingsService(db);

        var request = new UpdateSecuritySettingsRequest(true, 10, "zh-CN", false, 1, invalidHour);
        var result = await sut.UpdateSecuritySettingsAsync(request);

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(23)]
    public async Task UpdateSecuritySettingsAsync_WithValidThumbnailHour_ShouldPersist(int validHour)
    {
        await using var db = CreateDbContext();
        var sut = new SystemSettingsService(db);

        var request = new UpdateSecuritySettingsRequest(true, 10, "zh-CN", false, 1, validHour);
        var result = await sut.UpdateSecuritySettingsAsync(request);

        result.Succeeded.Should().BeTrue();
        var retrieved = await sut.GetThumbnailGenerationHourAsync();
        retrieved.Should().Be(validHour);
    }

    [Fact]
    public async Task GetThumbnailGenerationHourAsync_WhenNoSettings_ShouldReturnDefault()
    {
        await using var db = CreateDbContext();
        var sut = new SystemSettingsService(db);

        // 初始化设置（使用 GetSecuritySettingsAsync 触发 EnsureSettingsAsync）
        await sut.GetSecuritySettingsAsync();
        var hour = await sut.GetThumbnailGenerationHourAsync();

        hour.Should().Be(2); // 默认值
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-settings-{Guid.NewGuid():N}")
            .Options;
        return new AnyDropDbContext(options);
    }
}
