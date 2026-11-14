using AnyDrop.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AnyDrop.Services;

/// <summary>
/// 登录 / 初始化失败限流：同一来源连续失败达到阈值后锁定，冷却时间渐进递增。
///
/// 渐进规则（S0-03，替代原来固定的 60 秒锁全部来源）：
/// 第 n 次锁定冷却 <c>base × 2^(n-1)</c>，上限 <c>LoginMaxCooldownSeconds</c>
/// （默认 900 秒）。成功（<see cref="Reset"/>）后计数清零。
/// 锁定期内到达的失败不延长锁定；锁定过期后下一次失败从 1 重新计数，
/// 但保留已累计的锁定次数，因此反复攻击的冷却会越拉越长。
/// 条目 TTL 至少覆盖当前冷却期的两倍，使锁定次数能在两次锁定之间保留下来。
/// </summary>
public sealed class LoginRateLimiter(
    IMemoryCache memoryCache,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider) : ILoginRateLimiter
{
    private readonly int _maxFailures = Math.Max(1, authOptions.Value.LoginMaxFailures);
    private readonly int _cooldownSeconds = Math.Max(1, authOptions.Value.LoginCooldownSeconds);
    private readonly int _maxCooldownSeconds = Math.Max(1, authOptions.Value.LoginMaxCooldownSeconds);

    public bool IsLocked(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var cacheKey = BuildKey(key);
        if (!memoryCache.TryGetValue<LoginWindowState>(cacheKey, out var state) || state is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();

        // 注意：这里**不能**移除条目。
        //
        // LockedUntil 为 null 只表示「已有失败但尚未达到阈值」。而 IsLocked 会在每次
        // 登录尝试前被调用一次，若在此处移除条目，累计的失败次数就会被清零——
        // 于是 FailedCount 永远停留在 1，永远达不到阈值，限流彻底失效。
        // （这正是修复前的实际行为：默认阈值 5 时暴力破解完全不受限制。）
        //
        // 锁已过期时同样保留条目：RegisterFailure 需要据此保留退避历史，
        // 让连续触发锁定时冷却时间继续递增。条目的最终清理交给缓存过期与 Reset。
        if (state.LockedUntil is null || state.LockedUntil <= now)
        {
            return false;
        }

        retryAfter = state.LockedUntil.Value - now;
        return true;
    }

    public void RegisterFailure(string key)
    {
        var cacheKey = BuildKey(key);
        var state = memoryCache.Get<LoginWindowState>(cacheKey) ?? new LoginWindowState();
        var now = timeProvider.GetUtcNow();

        // 锁定期内不再计数：不延长本次锁定。
        if (state.LockedUntil is not null && state.LockedUntil > now)
        {
            return;
        }

        // 上一轮锁定已过期：失败计数归零，锁定次数保留用于渐进递增。
        if (state.LockedUntil is not null)
        {
            state.FailedCount = 0;
            state.LockedUntil = null;
        }

        state.FailedCount++;
        state.FirstFailedAt ??= now;

        var cooldownSeconds = _cooldownSeconds;
        if (state.FailedCount >= _maxFailures)
        {
            state.LockoutCount++;
            // 指数退避：每再触发一次锁定就把冷却时间翻倍（上限 LoginMaxCooldownSeconds，
            // 默认 900 秒）。用 long 计算避免移位溢出。
            var escalated = (long)_cooldownSeconds << Math.Min(state.LockoutCount - 1, 30);
            cooldownSeconds = (int)Math.Min(escalated, _maxCooldownSeconds);
            state.LockedUntil = now.AddSeconds(cooldownSeconds);
            // 重置计数：解锁后重新累计，而不是一次失败就立刻再次锁定
            state.FailedCount = 0;
        }

        // 缓存过期时间必须覆盖锁定期的末端。否则当退避延长到 5 分钟、10 分钟时，
        // 条目会先于锁定失效而被静默清除，限流形同虚设。
        var ttlSeconds = Math.Max(_cooldownSeconds * 2, cooldownSeconds * 2);
        memoryCache.Set(cacheKey, state, TimeSpan.FromSeconds(ttlSeconds));
    }

    public void Reset(string key) => memoryCache.Remove(BuildKey(key));

    private static string BuildKey(string key) => $"auth:login-failed:{key}";

    private sealed class LoginWindowState
    {
        public int FailedCount { get; set; }

        /// <summary>累计锁定次数（仅成功后清零），决定下一次锁定的冷却时长。</summary>
        public int LockoutCount { get; set; }
        public DateTimeOffset? FirstFailedAt { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
