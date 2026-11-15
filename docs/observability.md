# 可观测性：文件日志与每日摘要（S3-04）

## 文件日志

控制台日志之外再落一份文件日志（`AnyDrop/Logging/FileLoggerProvider.cs`，
只依赖 BCL，不引入新日志库）。

- 位置：`Logging:File:Directory`；留空时取数据库文件所在目录下的 `logs`
  （本地 `data/logs`，Docker 下 `/data/logs`，随数据卷持久化；
  备份脚本只备份 db / files / keys，**不备份日志**）。
- 文件：每天一个，按 UTC 日期命名（`anydrop-20261115.log`）；
  单文件超 `MaxFileSizeMB`（默认 10MB，1–100）后滚动为
  `anydrop-20261115.1.log`、`.2.log`……；超过 `RetainedDays`
  （默认 14 天，1–90）的旧文件在写入时顺带清理。
- 格式：`2026-11-15T03:00:00.1234567Z [Warning] Category: 消息`，
  一条日志一个逻辑行（消息里的换行折成空格），异常堆栈单独成行并缩进。
  行首是 ISO 8601 UTC 时间戳——每日摘要按它换算统计窗口。
- 配置非法（上下限越界）时启动即失败，与 S3-02 的配置校验一致。
- 日志写失败绝不拖垮应用：写入路径全程吞异常；日志目录建不出来时
  静默降级为纯控制台日志。

## 每日摘要

`AnyDrop/Services/DailySummaryService.cs`：每天后台任务时区凌晨 3 点
（与对账任务同一锚点）生成上一日摘要，写到日志目录的
`summaries/daily-summary-<yyyy-MM-dd>.md`（保留 90 天），同时在日志里记一行计数。
`DailySummary:Enabled=false` 可关闭。摘要先落文件，**不做推送告警**。

计数来源（复用 Stage 1 已有的对账与存储健康逻辑，不另起一套计数器）：

| 指标 | 来源 |
|---|---|
| 孤儿文件 / 缺失引用 / 清扫结果 | 报告模式对账 `ReconcileAsync(deleteOrphans: false)` |
| 待删除队列 | `PendingFileDeletions` 行数（摘要生成时刻） |
| 缩略图失败 / 清理失败 | 当日日志文件里对应标记的行数（见下） |
| FTS 索引一致性 | S3-03 `GetConsistencyAsync`（消息行 / 已索引） |

注意：摘要的对账是报告模式（不删孤儿文件），但与每日对账任务一样执行
已授权的生命周期清扫（超期预占位、到期延迟删除、待删除队列）——
清扫结果本身就是摘要的一部分。默认配置下（`OrphanCleanupEnabled=false`）
摘要与对账任务的行为一致。

失败统计的窗口是摘要日（后台任务时区）00:00–24:00；日志文件按 UTC 日期
切分，统计时按行首时间戳换算进窗口，跨 UTC 日界的窗口会读两个日志文件。
日志文件缺失（日志被禁用或刚装机）时失败计数记为 `n/a`，不影响其他计数；
FTS 不可用时一致性计数同样记为 `n/a`。

失败标记见 `AnyDrop/Logging/LogMarkers.cs`，与日志调用点共用常量——
字符串改一处就两处一起改，避免悄悄漂移：

- 缩略图失败：`Background thumbnail generation failed…`
- 清理失败：过期清理周期失败、自动清理失败、孤儿对账首轮/任务失败、
  过期分片会话清扫失败（共 5 条标记）。

## 运维用法

```bash
# 看今天的日志
tail -f /data/logs/anydrop-$(date -u +%Y%m%d).log

# 看昨天的摘要
cat /data/logs/summaries/daily-summary-$(date -d yesterday +%F).md
```

摘要里的数字异常时：孤儿/缺失去设置页的“存储健康”看修复入口；
缩略图/清理失败去当日日志文件里按标记 grep 看完整堆栈。
