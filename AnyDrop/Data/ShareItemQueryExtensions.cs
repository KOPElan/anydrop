using AnyDrop.Models;

namespace AnyDrop.Data;

/// <summary>
/// S0-06（DAT-05/06，D1）：过期消息读过滤。
///
/// 「定时销毁」的消息在 <c>ExpiresAt</c> 到达后必须立即对所有读路径不可见，
/// 不能等到下一轮清理任务才消失。因此每个读查询都要显式过滤过期项，
/// 而不是只依赖清理任务的删除/标记。
/// </summary>
internal static class ShareItemQueryExtensions
{
    /// <summary>只保留未到期的消息（<c>ExpiresAt</c> 为空或在未来）。</summary>
    public static IQueryable<ShareItem> NotExpired(this IQueryable<ShareItem> query, DateTimeOffset now)
        => query.Where(x => x.ExpiresAt == null || x.ExpiresAt > now);
}
