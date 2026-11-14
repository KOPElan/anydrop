namespace AnyDrop.Services;

/// <summary>
/// S3-03：FTS5 搜索索引的可用性状态（单例）。
///
/// 启动时由 <c>DatabaseMigrationExtensions.MigrateDatabaseAsync</c>
/// 根据索引创建结果设置；搜索中途发现索引不可用时
/// <see cref="ShareService"/> 调用 <see cref="MarkUnavailable"/>，
/// 本次启动内不再尝试 FTS，全程回退到 LIKE。
/// </summary>
public sealed class MessageSearchIndex
{
    /// <summary>FTS5 索引是否可用。默认 false（未创建或创建失败时即走 LIKE）。</summary>
    public bool IsAvailable { get; set; }

    public void MarkUnavailable() => IsAvailable = false;
}
