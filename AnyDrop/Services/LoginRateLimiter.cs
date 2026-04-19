using AnyDrop.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AnyDrop.Services;

public sealed class LoginRateLimiter(IMemoryCache memoryCache, IOptions<AuthOptions> authOptions) : ILoginRateLimiter
{
    private readonly int _maxFailures = Math.Max(1, authOptions.Value.LoginMaxFailures);
    private readonly int _cooldownSeconds = Math.Max(1, authOptions.Value.LoginCooldownSeconds);

    public bool IsLocked(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!memoryCache.TryGetValue<LoginWindowState>(BuildKey(key), out var state) || state is null)
        {
            return false;
        }

        if (state.LockedUntil is null || state.LockedUntil <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        retryAfter = state.LockedUntil.Value - DateTimeOffset.UtcNow;
        return true;
    }

    public void RegisterFailure(string key)
    {
        var cacheKey = BuildKey(key);
        var state = memoryCache.Get<LoginWindowState>(cacheKey) ?? new LoginWindowState();
        state.FailedCount++;
        state.FirstFailedAt ??= DateTimeOffset.UtcNow;

        if (state.FailedCount >= _maxFailures)
        {
            state.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(_cooldownSeconds);
        }

        memoryCache.Set(cacheKey, state, TimeSpan.FromSeconds(_cooldownSeconds * 2));
    }

    public void Reset(string key) => memoryCache.Remove(BuildKey(key));

    private static string BuildKey(string key) => $"auth:login-failed:{key}";

    private sealed class LoginWindowState
    {
        public int FailedCount { get; set; }
        public DateTimeOffset? FirstFailedAt { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
