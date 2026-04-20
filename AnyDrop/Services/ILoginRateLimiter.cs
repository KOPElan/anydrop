namespace AnyDrop.Services;

public interface ILoginRateLimiter
{
    bool IsLocked(string key, out TimeSpan retryAfter);
    void RegisterFailure(string key);
    void Reset(string key);
}
