namespace AnyDrop.Models;

/// <summary>
/// 删除策略常量（D5，2026-11-14 Elan 确认）。
/// </summary>
public static class DeletionPolicy
{
    /// <summary>
    /// 批量 / 自动清理的延迟物理删除天数：先写 <c>ShareItem.DeleteAfter</c>
    /// 标记（当前时间 + 7 天），消息立即不可见，文件到期后由清扫任务删除。
    /// 单条手动删除不走延迟，直接删行。
    /// </summary>
    public const int DelayedDeleteDays = 7;
}
