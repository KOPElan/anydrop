using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Data;

/// <summary>
/// S0-04（DAT-03/04）：写操作有限重试。
///
/// SQLite 单写者模型下，并发写入可能撞上 <c>SQLITE_BUSY</c>（5）/
/// <c>SQLITE_LOCKED</c>（6），即使 WAL + busy_timeout 已配，突发下仍可能
/// 冒出来。重试只针对这两个错误码：失败的 SQLite 事务会整体回滚，
/// 变更跟踪状态保留，因此重放 <c>SaveChangesAsync</c> 是安全的。
/// 3 次尝试，退避 100 / 200 / 400 ms；仍失败则原样抛出，不无限重试。
/// 注意：EF Core 会把提供程序异常包进 <c>DbUpdateException</c>，
/// 因此要拆 <c>InnerException</c> 里的 SqliteException 来判断错误码。
/// </summary>
public static class DbContextRetryExtensions
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(100);

    public static async Task<int> SaveChangesWithRetryAsync(this DbContext dbContext, CancellationToken ct = default)
    {
        var delay = InitialDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts && IsBusyOrLocked(ex))
            {
                await Task.Delay(delay, ct);
                delay *= 2;
            }
        }
    }

    internal static bool IsBusyOrLocked(DbUpdateException ex)
        => ex.InnerException is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 6;
}
