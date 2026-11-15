using AnyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Tests.Unit.TestDoubles;

/// <summary>
/// 测试用的 <see cref="IDbContextFactory{TContext}"/>。
///
/// S1-03 起服务端一律用 <c>IDbContextFactory&lt;AnyDropDbContext&gt;</c>、
/// 每次写操作建短寿命上下文；测试里直接 new 服务时也必须传工厂而不是
/// 一个长寿命上下文。InMemory 数据库按名称共享存储，所以用同一个工厂
/// 创建的上下文能看到彼此写入的数据（先用一个上下文播种、服务再用
/// 另一个上下文读取，与生产行为一致）。
/// </summary>
internal sealed class TestDbContextFactory : IDbContextFactory<AnyDropDbContext>
{
    private readonly DbContextOptions<AnyDropDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AnyDropDbContext> options) => _options = options;

    public AnyDropDbContext CreateDbContext() => new(_options);
}
