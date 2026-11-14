using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AnyDrop.Data;

/// <summary>
/// 供 <c>dotnet ef</c> 设计时使用（生成/检查迁移，如 S1-04 的基线迁移）。
/// 仅构造模型，不连真实数据库；运行时仍走 Program.cs 里的
/// <c>AddDbContextFactory</c>。连接串用内存库——脚手架只需要模型，
/// 不需要真实文件。
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AnyDropDbContext>
{
    public AnyDropDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new AnyDropDbContext(options);
    }
}
