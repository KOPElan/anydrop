using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AnyDrop.Data;

/// <summary>
/// S0-04（DAT-03/04）：SQLite 并发写防护。
///
/// 关键背景：Microsoft.Data.Sqlite 的连接串**不支持** <c>Journal Mode</c> /
/// <c>Busy Timeout</c> 关键字（实测直接抛 ArgumentException），因此计划里
/// 「连接串配 WAL + busy_timeout」的做法不可行，改用以下两步：
/// <list type="bullet">
/// <item>WAL：启动时对库文件执行一次 <c>PRAGMA journal_mode=WAL</c>（见
/// <see cref="DatabaseMigrationExtensions"/>），模式按文件持久化；</item>
/// <item>busy_timeout：按连接设置，由本拦截器在每次打开连接时执行
/// <c>PRAGMA busy_timeout=5000</c>。</item>
/// </list>
/// 再叠加 <see cref="DbContextRetryExtensions.SaveChangesWithRetryAsync"/>
/// 的有限重试（仅 busy / locked），三层共同兜底。
/// </summary>
public sealed class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    public const int BusyTimeoutMilliseconds = 5000;

    public override void ConnectionOpened(
        System.Data.Common.DbConnection connection,
        ConnectionEndEventData eventData)
    {
        ApplyBusyTimeout(connection);
    }

    public override async Task ConnectionOpenedAsync(
        System.Data.Common.DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqliteConnection sqliteConnection)
        {
            await using var command = sqliteConnection.CreateCommand();
            command.CommandText = $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        ApplyBusyTimeout(connection);
    }

    private static void ApplyBusyTimeout(System.Data.Common.DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};";
        command.ExecuteNonQuery();
    }
}
