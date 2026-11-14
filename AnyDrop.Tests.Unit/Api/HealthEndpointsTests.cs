using AnyDrop.Api;
using AnyDrop.Data;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        var factory = CreateDbContextFactory();

        var result = await HealthEndpoints.GetHealthAsync(
            factory, CreateConfiguration(_basePath), NullLoggerFactory.Instance, CancellationToken.None);

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task GetHealthAsync_Payload_ContainsVersion()
    {
        // S3-01：任一运行实例可凭 /health 的 version 字段查到对应标签与提交。
        var factory = CreateDbContextFactory();

        var result = await HealthEndpoints.GetHealthAsync(
            factory, CreateConfiguration(_basePath), NullLoggerFactory.Instance, CancellationToken.None);

        var json = await ExecuteToJsonAsync(result);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var version = doc.RootElement.GetProperty("version").GetString();
        version.Should().NotBeNullOrWhiteSpace();
        version.Should().Be(AppVersion.Current);
    }

    /// <summary>把 IResult 写进内存响应流，取回 JSON 文本（不断言整段 JSON，只取所需字段）。</summary>
    private static async Task<string> ExecuteToJsonAsync(IResult result)
    {
        // Ok<T>.ExecuteAsync 会从 RequestServices 取 JsonOptions，裸
        // DefaultHttpContext 没有服务提供方会抛 ArgumentNullException。
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task GetHealthAsync_StorageNotWritable_Returns503()
    {
        var factory = CreateDbContextFactory();

        // 把存储路径指向一个已存在的「文件」：Directory.CreateDirectory 会失败，
        // 从而模拟磁盘/卷不可写。比使用非法字符更贴近真实故障，且跨平台稳定。
        var blocker = Path.Combine(AppContext.BaseDirectory, $"health-blocker-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blocker, "x");
        try
        {
            var result = await HealthEndpoints.GetHealthAsync(
                factory, CreateConfiguration(blocker), NullLoggerFactory.Instance, CancellationToken.None);

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
        // 工厂返回一个已被释放的上下文：探针查询抛 ObjectDisposedException，应返回 503。
        var disposed = CreateDbContextFactory().CreateDbContext();
        await disposed.DisposeAsync();
        var factory = new DisposedContextFactory(disposed);

        var result = await HealthEndpoints.GetHealthAsync(
            factory, CreateConfiguration(_basePath), NullLoggerFactory.Instance, CancellationToken.None);

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    private static TestDbContextFactory CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-health-{Guid.NewGuid():N}")
            .Options;

        return new TestDbContextFactory(options);
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

    /// <summary>始终返回同一个（已被释放的）上下文的工厂，仅用于模拟数据库不可用。</summary>
    private sealed class DisposedContextFactory(AnyDropDbContext disposed) : IDbContextFactory<AnyDropDbContext>
    {
        public AnyDropDbContext CreateDbContext() => disposed;
    }
}