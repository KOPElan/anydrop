using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Data;

/// <summary>
/// S3-03：消息搜索的 SQLite FTS5 索引。
///
/// 结构：独立虚表 <c>ShareItemsFts</c>（不是外部内容表），索引
/// <c>ShareItems</c> 的 Content / FileName / LinkTitle / LinkDescription
/// 四列（与旧 LIKE 搜索覆盖的列一致），分词器 <c>unicode61</c>；
/// 另有一列 <c>ItemId UNINDEXED</c> 存消息 Guid（只存不索引），
/// 搜索先由虚表查出 ItemId 再关联原表。
///
/// 刻意不用外部内容（external content）表：实测发现它有三个坑——
///   * <c>COUNT(*) FROM 虚表</c> 读的是内容表行数，不是索引行数，
///     一致性校验会被误导；
///   * <c>INSERT INTO 虚表 SELECT ... WHERE rowid NOT IN
///     (SELECT rowid FROM 虚表)</c> 的回填永远插不进任何行（子查询
///     同样读内容表，全部行都被判定为“已存在”），索引实际是空的，
///     MATCH 查不到任何东西；
///   * 正确回填只能用 <c>rebuild</c> 全量重建，每次启动都重建不划算。
/// 独立表没有这些问题：COUNT(*)、回填、触发器都是普通表语义，
/// 代价是索引里多存一份文本——单用户量级下可忽略。
///
/// 同步：INSERT / UPDATE / DELETE 三个触发器随写随更；启动时
/// <see cref="EnsureAsync"/> 再做一次回填（补齐触发器建立前、
/// 或索引曾创建失败期间写入的行），索引漂移可自愈。
///
/// 分词器选择（实测，SQLite 3.45.1）：
///   * <c>unicode61</c>：词首前缀任意长度都有效（“你好*”命中“你好世界”，
///     “hello*”命中“Hello World”，大小写不敏感）；连续中文成一个词，
///     词中子串（“好世*”、“龙*”）命中不了。
///   * <c>trigram</c>：词中子串要 ≥3 字符才有效（“好世界*”命中，
///     “你好*”不命中）——两字中文查询是常用情形，不可用。
///   因此主分词器用 unicode61；词中子串的召回缺口由
///   <see cref="ShareService.SearchTopicMessagesAsync"/> 的 LIKE 兜底补齐
///   （FTS 结果不满一页时再跑一次 LIKE 并合并去重）。
///
/// 可用性：FTS5 是否启用取决于 SQLite 构建。创建失败时
/// <see cref="EnsureAsync"/> 只记 Warning 并返回 false，不影响启动；
/// 搜索随后全程走 LIKE（见 <see cref="Services.MessageSearchIndex"/>）。
/// </summary>
public static class MessageSearchFts
{
    public const string TableName = "ShareItemsFts";

    private const string CreateTableSql =
        "CREATE VIRTUAL TABLE IF NOT EXISTS \"ShareItemsFts\" USING fts5(" +
        "\"ItemId\" UNINDEXED, \"Content\", \"FileName\", \"LinkTitle\", \"LinkDescription\", " +
        "tokenize='unicode61');";

    private const string CreateInsertTriggerSql =
        "CREATE TRIGGER IF NOT EXISTS \"ShareItemsFts_ai\" AFTER INSERT ON \"ShareItems\" BEGIN " +
        "INSERT INTO \"ShareItemsFts\"(\"ItemId\", \"Content\", \"FileName\", \"LinkTitle\", \"LinkDescription\") " +
        "VALUES (new.\"Id\", new.\"Content\", new.\"FileName\", new.\"LinkTitle\", new.\"LinkDescription\"); " +
        "END;";

    private const string CreateDeleteTriggerSql =
        "CREATE TRIGGER IF NOT EXISTS \"ShareItemsFts_ad\" AFTER DELETE ON \"ShareItems\" BEGIN " +
        "DELETE FROM \"ShareItemsFts\" WHERE \"ItemId\" = old.\"Id\"; " +
        "END;";

    private const string CreateUpdateTriggerSql =
        "CREATE TRIGGER IF NOT EXISTS \"ShareItemsFts_au\" AFTER UPDATE ON \"ShareItems\" BEGIN " +
        "DELETE FROM \"ShareItemsFts\" WHERE \"ItemId\" = old.\"Id\"; " +
        "INSERT INTO \"ShareItemsFts\"(\"ItemId\", \"Content\", \"FileName\", \"LinkTitle\", \"LinkDescription\") " +
        "VALUES (new.\"Id\", new.\"Content\", new.\"FileName\", new.\"LinkTitle\", new.\"LinkDescription\"); " +
        "END;";

    private const string BackfillSql =
        "INSERT INTO \"ShareItemsFts\"(\"ItemId\", \"Content\", \"FileName\", \"LinkTitle\", \"LinkDescription\") " +
        "SELECT \"Id\", \"Content\", \"FileName\", \"LinkTitle\", \"LinkDescription\" FROM \"ShareItems\" " +
        "WHERE \"Id\" NOT IN (SELECT \"ItemId\" FROM \"ShareItemsFts\");";

    /// <summary>
    /// 建表 + 触发器 + 存量回填（幂等，可反复调用）。
    /// 返回 true 表示索引已可用；SQLite 非 FTS5 构建等失败时记 Warning 并返回 false。
    /// 非 SQLite provider（如测试的 InMemory）直接返回 false。
    /// </summary>
    public static async Task<bool> EnsureAsync(AnyDropDbContext db, ILogger? logger, CancellationToken ct = default)
    {
        if (!string.Equals(
                db.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
        {
            return false;
        }

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            foreach (var sql in new[] { CreateTableSql, CreateInsertTriggerSql, CreateDeleteTriggerSql, CreateUpdateTriggerSql })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(ct);
            }

            await using var backfill = connection.CreateCommand();
            backfill.CommandText = BackfillSql;
            var backfilled = await backfill.ExecuteNonQueryAsync(ct);

            var (total, indexed) = await GetConsistencyAsync(db, ct);
            logger?.LogInformation(
                "FTS5 消息搜索索引就绪：ShareItems {Total} 行，已索引 {Indexed} 行，本次回填 {Backfilled} 行。",
                total, indexed, backfilled);
            return true;
        }
        catch (SqliteException ex)
        {
            logger?.LogWarning(
                ex,
                "FTS5 消息搜索索引创建失败（当前 SQLite 构建可能未启用 FTS5），搜索将回退到 LIKE，功能不受影响、速度变慢。");
            return false;
        }
    }

    /// <summary>
    /// 索引一致性校验：ShareItems 总行数 vs FTS 索引行数。
    /// 注意：总数含已过期 / 已标记删除 / Uploading 的行——索引与之一一对应，
    /// 搜索时的可见性由查询条件过滤，不由索引行数体现。
    /// </summary>
    public static async Task<(long Total, long Indexed)> GetConsistencyAsync(AnyDropDbContext db, CancellationToken ct = default)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var totalCommand = connection.CreateCommand();
        totalCommand.CommandText = "SELECT COUNT(*) FROM \"ShareItems\";";
        var total = Convert.ToInt64(await totalCommand.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);

        await using var indexedCommand = connection.CreateCommand();
        indexedCommand.CommandText = "SELECT COUNT(*) FROM \"ShareItemsFts\";";
        var indexed = Convert.ToInt64(await indexedCommand.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);

        return (total, indexed);
    }

    /// <summary>
    /// 把用户查询转成 FTS5 MATCH 表达式。
    ///
    /// 做法：按「非字母/数字」切词（“Quarterly-Report”切成 Quarterly、Report），
    /// 每词做词首前缀查询（<c>"词"*</c>），词间为空格（FTS5 里即 AND）。
    /// 前缀查询保留了旧 LIKE 的「输入越打越准、打一半也能搜到」的手感，
    /// 且大小写不敏感（unicode61 自带折叠）。
    /// 查询全是符号/空白、切不出任何词时返回 null（调用方直接走 LIKE）。
    /// </summary>
    public static string? BuildMatchQuery(string query)
    {
        var terms = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                terms.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            terms.Add(current.ToString());
        }

        if (terms.Count == 0)
        {
            return null;
        }

        // 词本身只剩字母/数字，不含 FTS5 语法字符；引号转义只是防御性处理。
        return string.Join(" ", terms.Select(t => $"\"{t.Replace("\"", "\"\"", StringComparison.Ordinal)}\"*"));
    }

    /// <summary>
    /// FTS5 搜索：返回匹配的消息 Id（时间倒序，最多 <paramref name="take"/> 条）。
    ///
    /// 可见性条件（未过期、未标记删除、已落盘）必须在 SQL 里复写一遍——
    /// 原生 SQL 不走 EF 的全局查询过滤器。
    ///
    /// 两个格式陷阱（均已实测）：
    ///   * Guid：EF Core SQLite 把 Guid 存成大写 TEXT（如
    ///     <c>2E32693E-…</c>），而 <c>Guid.ToString()</c> 是小写；
    ///     SQLite TEXT 比较区分大小写，小写参数永远匹配不到。
    ///     因此 TopicId 参数必须转大写。
    ///   * DateTimeOffset：EF 存成 UTC 的 <c>yyyy-MM-dd HH:mm:ss</c>
    ///     文本（如 <c>2026-11-15 02:30:00</c>），而原生参数会按 ISO 8601
    ///     带 T 与时区存（如 <c>2026-11-14T22:16:41.2901923+00:00</c>）；
    ///     两种格式混在一起做字符串比较，同日内的先后会被空格与 T 的
    ///     大小关系带偏。因此时间比较改走 <c>strftime('%s')</c> 取 epoch
    ///     秒再比整数，不依赖文本格式。
    /// </summary>
    public static async Task<List<Guid>> SearchIdsAsync(
        AnyDropDbContext db,
        Guid topicId,
        string matchQuery,
        int take,
        DateTimeOffset? before,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        const string sql =
            "SELECT f.\"ItemId\" FROM \"ShareItemsFts\" AS f " +
            "INNER JOIN \"ShareItems\" AS s ON s.\"Id\" = f.\"ItemId\" " +
            "WHERE \"ShareItemsFts\" MATCH $match " +
            "AND s.\"TopicId\" = $topic " +
            "AND s.\"DeleteAfter\" IS NULL " +
            "AND s.\"UploadState\" = 1 " +
            "AND (s.\"ExpiresAt\" IS NULL OR CAST(strftime('%s', s.\"ExpiresAt\") AS INTEGER) > $now) " +
            "AND ($before IS NULL OR CAST(strftime('%s', s.\"CreatedAt\") AS INTEGER) < $before) " +
            "ORDER BY s.\"CreatedAt\" DESC LIMIT $take;";
        var parameters = new[]
        {
            // Guid 存大写：见上方格式陷阱。
            new SqliteParameter("$topic", topicId.ToString("D").ToUpperInvariant()),
            new SqliteParameter("$match", matchQuery),
            new SqliteParameter("$now", now.ToUnixTimeSeconds()),
            new SqliteParameter("$before", before.HasValue ? (object)before.Value.ToUnixTimeSeconds() : DBNull.Value),
            new SqliteParameter("$take", take)
        };
        return await db.Database.SqlQueryRaw<Guid>(sql, parameters).ToListAsync(ct);
    }

    /// <summary>
    /// 判断异常是否属于「FTS 索引不可用」（表不存在、模块未启用），
    /// 是则搜索应回退到 LIKE；其他异常（如磁盘损坏）不应被吞掉。
    /// </summary>
    public static bool IsFtsUnavailable(Exception ex)
        => ex is SqliteException sqlite
            && (sqlite.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                || sqlite.Message.Contains("no such module", StringComparison.OrdinalIgnoreCase));
}
