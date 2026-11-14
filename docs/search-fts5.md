# 消息搜索 FTS5（S3-03）

## 结构

- 虚表 `ShareItemsFts`（独立表，不是外部内容表），分词器 `unicode61`，
  索引 Content / FileName / LinkTitle / LinkDescription 四列
  （与旧 LIKE 搜索覆盖的列一致），另有一列 `ItemId UNINDEXED` 存消息 Guid。
- 同步：INSERT / UPDATE / DELETE 三个触发器随写随更；
  启动时 `MessageSearchFts.EnsureAsync` 再做一次回填（幂等，只补缺失行）。
- 一致性校验：`GetConsistencyAsync` 对比 `ShareItems` 与 `ShareItemsFts`
  的行数（S3-04 的每日摘要会引用这个数字）。
- 可用性：FTS5 是否启用取决于 SQLite 构建。创建失败时启动只记 Warning，
  搜索全程走 LIKE；`MessageSearchIndex.IsAvailable` 记录当前状态，
  搜索中途发现索引不可用时也会自动降级（本次启动内不再尝试 FTS）。

## 为什么是独立表，不是外部内容表

实测发现外部内容表有三个坑（SQLite 3.45.1）：

1. `COUNT(*) FROM 虚表` 读的是内容表行数，不是索引行数——一致性校验会被误导；
2. `INSERT INTO 虚表 SELECT ... WHERE rowid NOT IN (SELECT rowid FROM 虚表)`
   的回填永远插不进任何行（子查询同样读内容表，全部行都被判定为“已存在”），
   索引实际是空的，MATCH 查不到任何东西；
3. 正确回填只能用 `rebuild` 全量重建，每次启动都重建不划算。

独立表没有这些问题：COUNT(*)、回填、触发器都是普通表语义。
代价是索引里多存一份文本——单用户量级下可忽略。
（教训：第一版按外部内容表实现时，单元测试全绿——因为 LIKE 兜底悄悄
补上了 FTS 的空结果；是直接测 `SearchIdsAsync` 的用例抓到了这个问题。
此后凡是“主路径 + 兜底”的实现，必须有绕开兜底直接测主路径的用例。）

## 查询语义：FTS 为主、LIKE 兜底

- 用户查询按“非字母/数字”切词，每词做词首前缀查询（`"词"*`），词间为 AND。
  大小写不敏感（unicode61 自带折叠），与旧 LIKE 行为一致。
- FTS 结果不满一页时再跑一次 LIKE 并合并去重——补齐 unicode61
  命中不了的中文词中子串（如“好世”搜“你好世界”、单字“龙”）。
  FTS 满页时不再跑 LIKE 全表扫描，否则 FTS 的速度收益就没了。
- 翻页（`before` 游标）与可见性过滤（未过期、未标记删除、已落盘）
  在 FTS 与 LIKE 两路保持一致。

## 分词器选择（实测）

| 查询 | unicode61 | trigram |
|---|---|---|
| `你好*`（词首，两字） | 命中 | 不命中（词需 ≥3 字） |
| `好世*` / `龙*`（词中） | 不命中 | 不命中（<3 字） |
| `好世界*`（词中，三字） | 不命中 | 命中 |
| `hello*` / `HELLO*` | 命中（大小写不敏感） | 命中 |

结论：主分词器用 unicode61（两字中文查询是常用情形，trigram 覆盖不了）；
词中子串由 LIKE 兜底补齐。

## 原生 SQL 的两个格式陷阱（均已实测）

FTS 的 Id 查询是原生 SQL，不走 EF 的类型转换：

1. **Guid**：EF Core SQLite 把 Guid 存成大写 TEXT（如 `2E32693E-…`），
   而 `Guid.ToString()` 是小写；SQLite TEXT 比较区分大小写，
   小写参数永远匹配不到。参数必须转大写。
2. **DateTimeOffset**：EF 存成 UTC 的 `yyyy-MM-dd HH:mm:ss` 文本，
   而原生参数会按 ISO 8601 带 T 与时区存；两种格式混在一起做字符串比较，
   同日内的先后会被空格与 T 的大小关系带偏。
   因此时间比较改走 `strftime('%s')` 取 epoch 秒再比整数，不依赖文本格式。

以后再写原生 SQL 涉及 Guid / 时间时，必须按这两条处理。

## 万级数据耗时对比（留档）

数据：10,001 行真实 SQLite 文件，limit 100，每种查询 20 次取平均，
本机 VM（2026-11-14），测试 `MessageSearchFtsTests.Benchmark_10kRows_FtsVsLike`。

端到端（C#，含 ShareService 两路查询与 DTO 加载）：

| 查询 | FTS 主路径 | 旧 LIKE | 倍数 |
|---|---|---|---|
| common（`report`，命中 1000 行） | 15.1 ms | 2.9 ms | x0.2 |
| rare（`zebra-7741`，命中 1 行） | 8.3 ms | 9.1 ms | x1.1 |
| cjk（`冰霜`，命中 400 行） | 14.6 ms | 6.1 ms | x0.4 |

同数据原生 SQL（Python sqlite3，无 EF 开销）：

| 查询 | FTS | LIKE |
|---|---|---|
| common | 1.9 ms | 0.4 ms |
| rare | 0.0 ms | 3.3 ms |

解读（实话实说）：

- 万级数据下两者都很快（毫秒级），FTS 在本轮并没有跑赢 LIKE。
  密集查询上 LIKE 占优：它能用 CreatedAt 索引按时间倒序扫、凑够一页
  就停；FTS 要先把全部匹配排好序再取页。
- 稀疏查询上 FTS 的 MATCH 本身是压倒性胜利（0.0ms vs 3.3ms），
  但端到端数字被 LIKE 兜底拉平了：FTS 结果不满一页时兜底跑了一次
  LIKE 全表扫描——召回优先的代价。
- FTS 的收益在更大规模：LIKE 的稀疏查询耗时随表行数线性增长
  （每行 4 列 ToLower + LIKE），FTS 的 MATCH 不随表行数增长。
  万级这个量级还看不出差距，十万/百万级才会拉开。
- 后续可优化的方向（本轮不做）：FTS 的 Id 查询与 DTO 加载合并为
  一条原生 SQL（省一次往返）；稀疏查询的 LIKE 兜底改为“仅 FTS
  返回 0 行时跑”（召回换速度，需另行决策）。

## 回滚

删虚表与三个触发器即回到纯 LIKE（`DROP TABLE ShareItemsFts;`
加三条 `DROP TRIGGER`），或把 `MessageSearchIndex.IsAvailable`
置 false。数据本身不受影响。
