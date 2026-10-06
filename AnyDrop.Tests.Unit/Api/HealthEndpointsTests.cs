using AnyDrop.Api;
using AnyDrop.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnyDrop.Tests.Unit.Api;

/// <summary>
/// <c>/health</c> 端点的测试。
///
/// 该端点此前并不存在：容器编排只会请求首页，而首页返回 200 仅说明 Kestrel 起来了，
/// 数据库损坏或卷写满时依然会被判定为健康。
/// </summary>
public class HealthEndpointsTests : IDisposable
{
    private readonly string _basePath =
        Path.Combine(AppContext.BaseDirectory, "test-health", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetHealthAsync_HealthyDatabaseAndStorage_Returns200()
    {
        await using var db = CreateDbContext();

        var result = await HealthEndpoints.GetHealthAsync(
            db, CreateConfiguration(_basePath), NullLoggerFactory.Instance, CancellationToken.None);

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task GetHealthAsync_StorageNotWritable_Returns503()
    {
        await using var db = CreateDbContext();

        // 把存储路径指向一个已存在的「文件」：Directory.CreateDirectory 会失败，
        // 从而模拟磁盘/卷不可写。比使用非法字符更贴近真实故障，且跨平台稳定。
        var blocker = Path.Combine(AppContext.BaseDirectory, $"health-blocker-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blocker, "x");
        try
        {
            var result = await HealthEndpoints.GetHealthAsync(
                db, CreateConfiguration(blocker), NullLoggerFactory.Instance, CancellationToken.None);

            (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public async Task GetHealthAsync_DatabaseUnavailable_Returns503()
    {
        var db = CreateDbContext();
        await db.DisposeAsync();

        var result = await HealthEndpoints.GetHealthAsync(
            db, CreateConfiguration(_basePath), NullLoggerFactory.Instance, CancellationToken.None);

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-health-{Guid.NewGuid():N}")
            .Options;

        return new AnyDropDbContext(options);
    }

    private static IConfiguration CreateConfiguration(string basePath)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = basePath
            })
            .Build();

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, true);
        }
    }
}
