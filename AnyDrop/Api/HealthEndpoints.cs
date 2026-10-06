using AnyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Api;

/// <summary>
/// 健康检查端点。
///
/// 存在的理由：容器编排此前只会请求首页 <c>/</c>，只要应用能返回登录页就判定 healthy。
/// 数据库损坏、卷写满、存储目录不可写这些真正会导致服务不可用的情况完全不会被发现。
/// 这里执行两个真实探针：数据库可查询、存储目录可写。
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealthAsync)
            .AllowAnonymous()
            .WithTags("Health")
            .WithSummary("健康检查：验证数据库可查询与文件存储可写");

        return app;
    }

    public static async Task<IResult> GetHealthAsync(
        AnyDropDbContext dbContext,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("AnyDrop.Api.Health");
        var checks = new Dictionary<string, string>();
        var healthy = true;

        // 探针 1：真正查询一次。只调用 CanConnectAsync 无法发现「表缺失 / 库损坏」。
        try
        {
            _ = await dbContext.SystemSettings
                .AsNoTracking()
                .Select(x => x.Id)
                .FirstOrDefaultAsync(ct);
            checks["database"] = "ok";
        }
        catch (Exception ex)
        {
            healthy = false;
            checks["database"] = $"error: {ex.GetType().Name}";
            logger.LogError(ex, "健康检查：数据库探针失败。");
        }

        // 探针 2：确认存储目录可写。磁盘写满时这一步会失败，而写满会导致上传与
        // SQLite 写入一起失败，是自托管场景最常见的整体故障。
        try
        {
            var basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");
            Directory.CreateDirectory(basePath);
            var probeFile = Path.Combine(basePath, $".health-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probeFile, string.Empty, ct);
            File.Delete(probeFile);
            checks["storage"] = "ok";
        }
        catch (Exception ex)
        {
            healthy = false;
            checks["storage"] = $"error: {ex.GetType().Name}";
            logger.LogError(ex, "健康检查：存储探针失败。");
        }

        // 只返回状态摘要，不回显任何路径或配置，避免无意泄露。
        var payload = new { status = healthy ? "healthy" : "unhealthy", checks };

        return healthy
            ? Results.Ok(payload)
            : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
