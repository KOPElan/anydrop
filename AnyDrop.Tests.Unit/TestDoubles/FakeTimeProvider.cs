namespace AnyDrop.Tests.Unit.TestDoubles;

/// <summary>
/// 可控的假时钟，用于确定性地测试时间相关逻辑。
///
/// 只伪造「读取当前时间」（<see cref="GetUtcNow"/>），不伪造定时器：
/// 若要测试依赖 <c>Task.Delay(..., TimeProvider, ...)</c> 或 <c>PeriodicTimer</c> 的
/// 后台循环，还需要额外重写 <see cref="TimeProvider.CreateTimer"/>。
/// 当前用途是替代测试中的真实 <c>Thread.Sleep</c>。
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset? start = null)
        => _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>当前假时间（UTC）。</summary>
    public DateTimeOffset UtcNow => _now;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>向前推进时间。</summary>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "假时钟只支持向前推进。");
        }

        _now += delta;
    }

    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));
}
