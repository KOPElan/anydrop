using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// 应用 EF Core 迁移。
    ///
    /// 这里原本还包含两段历史遗留逻辑，均已移除：
    ///
    /// 1. 一段运行时 <c>ALTER TABLE SystemSettings ADD COLUMN
    ///    ScheduledThumbnailGenerationEnabled</c> 的 DDL。对应的正式迁移
    ///    （20260513141000_AddScheduledThumbnailGenerationFlag）早已存在，
    ///    而这段 DDL 不写入 __EFMigrationsHistory，会让数据库实际结构与迁移历史脱节，
    ///    未来任何触及同一列的迁移都可能失败。
    ///
    /// 2. 一段「SystemSettings 为空则插入默认行」的种子逻辑。默认行由
    ///    <see cref="AnyDropDbContext.OnModelCreating"/> 中的 HasData 声明并随迁移写入，
    ///    因此该分支永远不会执行（同一行在 SystemSettingsService.EnsureSettingsAsync
    ///    里还有一份防御性兜底）。三处重复且字段取值不一致，改默认值时极易漏改。
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken ct = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnyDropDbContext>();

        await db.Database.MigrateAsync(ct);
    }
}
