using AnyDrop.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AnyDrop.Services;

public sealed class LoginRateLimiter(
    IMemoryCache memoryCache,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider) : ILoginRateLimiter
{
    /// <summary>退避时长的上限，避免把合法用户长期锁在外面。</summary>
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(15);

    private readonly int _maxFailures = Math.Max(1, authOptions.Value.LoginMaxFailures);
    private readonly int _cooldownSeconds = Math.Max(1, authOptions.Value.LoginCooldownSeconds);

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

        if (state.LockedUntil is not null && state.LockedUntil <= now)
        {
            // 上一轮锁定已过期：保留 LockoutCount，让退避继续递增
            state.LockedUntil = null;
            state.FailedCount = 0;
        }

        state.FailedCount++;
        state.FirstFailedAt ??= now;

        if (state.FailedCount >= _maxFailures)
        {
            // 指数退避：每再触发一次锁定就把冷却时间翻倍（上限 15 分钟）。
            // 这让暴力破解的代价随时间快速上升，而普通用户输错几次几乎无感。
            state.LockoutCount++;
            var seconds = Math.Min(
                _cooldownSeconds * Math.Pow(2, state.LockoutCount - 1),
                MaxCooldown.TotalSeconds);

            state.LockedUntil = now.AddSeconds(seconds);
            // 重置计数：解锁后重新累计，而不是一次失败就立刻再次锁定
            state.FailedCount = 0;
        }

        // 缓存过期时间必须覆盖锁定期的末端。否则当退避延长到 5 分钟、10 分钟时，
        // 条目会先于锁定失效而被静默清除，限流形同虚设。
        var ttl = state.LockedUntil is { } until && until > now
            ? until - now + TimeSpan.FromSeconds(_cooldownSeconds)
            : TimeSpan.FromSeconds(_cooldownSeconds * 2);

        memoryCache.Set(cacheKey, state, ttl);
    }

    public void Reset(string key) => memoryCache.Remove(BuildKey(key));

    private static string BuildKey(string key) => $"auth:login-failed:{key}";

    private sealed class LoginWindowState
    {
        public int FailedCount { get; set; }
        public int LockoutCount { get; set; }
        public DateTimeOffset? FirstFailedAt { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
