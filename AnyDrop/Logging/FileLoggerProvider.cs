using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Logging;

/// <summary>
/// 文件日志配置（<c>Logging:File</c> 节）。
///
/// 默认目录：数据库文件所在目录下的 <c>logs</c>（本地 <c>data/logs</c>，
/// Docker 下 <c>/data/logs</c>）——随数据卷持久化，但备份脚本只备份
/// db / files / keys，不备份日志。
/// </summary>
public sealed class FileLoggerOptions
{
    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } = "logs";

    /// <summary>单个日志文件上限（MB），1–100，默认 10。</summary>
    public int MaxFileSizeMB { get; set; } = 10;

    /// <summary>日志保留天数，1–90，默认 14。</summary>
    public int RetainedDays { get; set; } = 14;

    public long MaxFileSizeBytes => (long)MaxFileSizeMB * 1024 * 1024;

    /// <summary>当天（UTC 日期）的日志文件名。</summary>
    public string FileNameFor(DateOnly utcDate) => $"anydrop-{utcDate:yyyyMMdd}.log";
}

/// <summary>
/// 日志里用于计数的稳定标记（S3-04）。
///
/// 每日摘要按这些字符串统计当日失败次数，因此日志调用点与摘要计数
/// 共用常量——字符串改一处就两处一起改，避免悄悄漂移。
/// </summary>
public static class LogMarkers
{
    /// <summary>后台缩略图生成失败（ShareService）。</summary>
    public const string ThumbnailFailed = "Background thumbnail generation failed for item {ItemId}";

    /// <summary>
    /// <see cref="ThumbnailFailed"/> 在日志行里的实际前缀（{ItemId} 已被替换成真实 Id）。
    /// 摘要按此外缀计数。
    /// </summary>
    public const string ThumbnailFailedPrefix = "Background thumbnail generation failed";

    /// <summary>过期消息清理周期失败（ExpiredMessageCleanupService）。</summary>
    public const string ExpiredCleanupFailed = "Expired message cleanup cycle failed.";

    /// <summary>自动清理旧消息检查失败（ExpiredMessageCleanupService）。</summary>
    public const string AutoCleanupFailed = "Auto cleanup cycle failed.";

    /// <summary>孤儿文件对账首轮失败（OrphanedFileCleanupService）。</summary>
    public const string OrphanReconcileFirstRunFailed = "孤儿文件对账首轮失败，宿主继续运行，将在下一锚定时刻重试。";

    /// <summary>孤儿文件对账任务失败（OrphanedFileCleanupService）。</summary>
    public const string OrphanReconcileFailed = "孤儿文件对账任务失败，将在下一锚定时刻重试。";

    /// <summary>过期分片会话清扫失败（OrphanedFileCleanupService）。</summary>
    public const string ExpiredUploadSweepFailed = "过期分片会话清扫失败，将在下一轮重试。";

    /// <summary>清理失败类标记（过期清理 + 孤儿对账 + 分片清扫）。</summary>
    public static readonly string[] CleanupFailedMarkers =
    [
        ExpiredCleanupFailed,
        AutoCleanupFailed,
        OrphanReconcileFirstRunFailed,
        OrphanReconcileFailed,
        ExpiredUploadSweepFailed,
    ];
}

/// <summary>
/// 文件日志提供方（S3-04）：控制台之外再落一份文件日志。
///
/// - 每天一个文件，按 UTC 日期命名（<c>anydrop-20261115.log</c>），
///   摘要按 UTC 日切分统计时与之一致；
/// - 单文件超限后滚动为 <c>.1.log</c>、<c>.2.log</c>……；
/// - 超过保留天数的旧文件在写入时顺带清理；
/// - 只依赖 BCL，不引入新日志库：日志是基础设施，单用户量级下
///   同步写 + 锁足够，且避免多一个包的供应链与配置面。
/// 日志写失败绝不能拖垮应用：写入路径全程吞异常。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggerOptions _options;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private DateOnly _writerDate;
    private DateTime _lastRetentionCheck = DateTime.MinValue;
    private bool _broken;

    public FileLoggerProvider(FileLoggerOptions options)
    {
        _options = options;
        if (!options.Enabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(options.Directory);
        }
        catch (Exception)
        {
            // 目录都建不出来就静默降级为纯控制台日志，不影响启动。
            _broken = true;
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal void Write(string categoryName, LogLevel level, string message, Exception? exception)
    {
        if (_broken || !_options.Enabled)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                EnsureWriterLocked();
                if (_writer is null)
                {
                    return;
                }

                // 一条日志一个逻辑行（消息里的换行折成空格），便于 grep 与摘要计数；
                // 异常堆栈单独成行、每行缩进 4 格。
                var flatMessage = message.ReplaceLineEndings(" ");
                var timestamp = DateTimeOffset.UtcNow.ToString("o");
                _writer.WriteLine($"{timestamp} [{level}] {categoryName}: {flatMessage}");
                if (exception is not null)
                {
                    foreach (var line in exception.ToString().Split('\n'))
                    {
                        _writer.WriteLine($"    {line.TrimEnd('\r')}");
                    }
                }

                _writer.Flush();
            }
        }
        catch (Exception)
        {
            // 日志写失败绝不抛到业务代码里；下次写入仍会重试。
            // 若是磁盘满了，重复尝试也不会成功，但也不应为此崩溃。
        }
    }

    private void EnsureWriterLocked()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (_writer is not null && _writerDate == today)
        {
            // 文件未超限才继续用当前 writer；超限则滚动。
            if (new FileInfo(CurrentPath()).Length < _options.MaxFileSizeBytes)
            {
                return;
            }

            RollLocked();
        }

        _writer?.Dispose();
        _writer = null;

        try
        {
            Directory.CreateDirectory(_options.Directory);
            _writer = new StreamWriter(CurrentPath(), append: true) { AutoFlush = true };
            _writerDate = today;
        }
        catch (Exception)
        {
            _broken = true;
            return;
        }

        MaybePruneOldFilesLocked();
    }

    private string CurrentPath() => Path.Combine(
        _options.Directory,
        _options.FileNameFor(DateOnly.FromDateTime(DateTime.UtcNow)));

    private void RollLocked()
    {
        _writer?.Dispose();
        _writer = null;

        var current = CurrentPath();
        // 找第一个未被占用的滚动序号（anydrop-20261115.1.log、.2.log……），上限 100；
        // 真写满 100 个说明配置的单文件上限太小，届时直接覆盖写回主文件。
        var stem = current[..^".log".Length];
        for (var n = 1; n <= 100; n++)
        {
            var rolled = $"{stem}.{n}.log";
            if (File.Exists(rolled))
            {
                continue;
            }

            try
            {
                File.Move(current, rolled);
            }
            catch (Exception)
            {
                // 移动失败就放弃滚动，继续往主文件追加。
            }

            return;
        }
    }

    private void MaybePruneOldFilesLocked()
    {
        // 每小时最多检查一次，避免每次写日志都扫目录。
        var now = DateTime.UtcNow;
        if (now - _lastRetentionCheck < TimeSpan.FromHours(1))
        {
            return;
        }

        _lastRetentionCheck = now;
        var cutoff = now.AddDays(-_options.RetainedDays);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_options.Directory, "anydrop-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception)
                {
                    // 单个文件删不掉不影响其他文件。
                }
            }
        }
        catch (Exception)
        {
            // 目录不可读也不影响日志写入本身。
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}

/// <summary>文件日志的配置解析与注册（S3-04）。</summary>
public static class FileLogging
{
    /// <summary>
    /// 从配置解析 <see cref="FileLoggerOptions"/> 并做上下限校验。
    /// 目录未配置时取数据库文件所在目录下的 <c>logs</c>。
    /// </summary>
    public static FileLoggerOptions ResolveOptions(IConfiguration configuration)
    {
        var options = new FileLoggerOptions
        {
            Enabled = configuration.GetValue("Logging:File:Enabled", true),
            MaxFileSizeMB = configuration.GetValue("Logging:File:MaxFileSizeMB", 10),
            RetainedDays = configuration.GetValue("Logging:File:RetainedDays", 14),
        };

        var configuredDirectory = configuration["Logging:File:Directory"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            options.Directory = configuredDirectory;
        }
        else
        {
            var dbPath = configuration["Storage:DatabasePath"] ?? "data/anydrop.db";
            var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            options.Directory = string.IsNullOrWhiteSpace(dbDirectory)
                ? "logs"
                : Path.Combine(dbDirectory, "logs");
        }

        if (options.MaxFileSizeMB is < 1 or > 100)
        {
            throw new InvalidOperationException(
                $"Logging:File:MaxFileSizeMB 必须在 1-100 之间，当前值 {options.MaxFileSizeMB}。");
        }

        if (options.RetainedDays is < 1 or > 90)
        {
            throw new InvalidOperationException(
                $"Logging:File:RetainedDays 必须在 1-90 之间，当前值 {options.RetainedDays}。");
        }

        return options;
    }

    /// <summary>注册文件日志（保留控制台日志）。配置非法时启动即失败。</summary>
    public static ILoggingBuilder AddFileLogging(this ILoggingBuilder builder, IConfiguration configuration)
    {
        var options = ResolveOptions(configuration);
        builder.Services.AddSingleton(options);
        if (options.Enabled)
        {
            builder.AddProvider(new FileLoggerProvider(options));
        }

        return builder;
    }
}
