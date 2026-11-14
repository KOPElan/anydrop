namespace AnyDrop.Tests.Unit.TestDoubles;

/// <summary>
/// 可控的假时钟，用于确定性地测试时间相关逻辑。
///
/// 伪造两件事：
/// <list type="bullet">
///   <item>「读取当前时间」（<see cref="GetUtcNow"/>）；</item>
///   <item>定时器（<see cref="TimeProvider.CreateTimer"/>）：<c>Task.Delay(..., TimeProvider, ...)</c>
///   与 <c>PeriodicTimer</c> 的到期都由 <see cref="Advance"/> 驱动，不耗真实时间。</item>
/// </list>
/// 推进时到期定时器的回调在调用线程上同步触发；回调里完成的 Task 续体
/// 仍走线程池，测试里推进后需给一次 <c>await Task.Delay(...)</c>（真实时间）
/// 让后台循环跑起来。
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset? start = null)
        => _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>当前假时间（UTC）。</summary>
    public DateTimeOffset UtcNow => _now;

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state, dueTime, period);
        lock (_lock)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>向前推进时间，并同步触发所有到期的定时器。</summary>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "假时钟只支持向前推进。");
        }

        _now += delta;
        FireDueTimers();
    }

    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));

    private void FireDueTimers()
    {
        // 循环重扫：回调可能新建定时器，周期定时器会重排下一次到期。
        while (true)
        {
            FakeTimer? next = null;
            lock (_lock)
            {
                foreach (var timer in _timers)
                {
                    if (timer.IsDisposed)
                    {
                        continue;
                    }

                    if (timer.DueUtc <= _now && (next is null || timer.DueUtc < next.DueUtc))
                    {
                        next = timer;
                    }
                }
            }

            if (next is null)
            {
                return;
            }

            next.Fire();
        }
    }

    private void RemoveTimer(FakeTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly FakeTimeProvider _provider;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period;
        private bool _disposed;

        public FakeTimer(FakeTimeProvider provider, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _provider = provider;
            _callback = callback;
            _state = state;
            _period = period;
            DueUtc = dueTime == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : provider._now + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
        }

        public DateTimeOffset DueUtc { get; private set; }

        public bool IsDisposed => _disposed;

        public void Fire()
        {
            if (_disposed)
            {
                return;
            }

            if (_period == Timeout.InfiniteTimeSpan)
            {
                _disposed = true;
                _provider.RemoveTimer(this);
            }
            else
            {
                DueUtc += _period;
            }

            _callback(_state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            _period = period;
            DueUtc = dueTime == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : _provider._now + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            _provider.RemoveTimer(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
