using Microsoft.Extensions.Caching.Memory;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

public class LoginRateLimiter(IMemoryCache cache) : ILoginRateLimiter
{
    private const int MaxAttemptsPerWindow = 5;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);
    private readonly Lock _syncLock = new();

    public bool IsAllowed(string? clientIp, string? identifier = null)
    {
        lock (_syncLock)
        {
            if (GetAttemptCount(GetIpKey(clientIp)) >= MaxAttemptsPerWindow)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(identifier) &&
                GetAttemptCount(GetIdentifierKey(identifier)) >= MaxAttemptsPerWindow)
            {
                return false;
            }

            return true;
        }
    }

    public void RecordFailedAttempt(string? clientIp, string? identifier = null)
    {
        lock (_syncLock)
        {
            IncrementAttemptCount(GetIpKey(clientIp));

            if (!string.IsNullOrWhiteSpace(identifier))
            {
                IncrementAttemptCount(GetIdentifierKey(identifier));
            }
        }
    }

    public void ResetAttempts(string? clientIp, string? identifier = null)
    {
        lock (_syncLock)
        {
            cache.Remove(GetIpKey(clientIp));

            if (!string.IsNullOrWhiteSpace(identifier))
            {
                cache.Remove(GetIdentifierKey(identifier));
            }
        }
    }

    private int GetAttemptCount(string key) =>
        cache.TryGetValue(key, out int count) ? count : 0;

    private void IncrementAttemptCount(string key)
    {
        var current = GetAttemptCount(key);
        cache.Set(key, current + 1, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = WindowDuration
        });
    }

    private static string GetIpKey(string? clientIp) =>
        $"login_ratelimit:ip:{(string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim())}";

    private static string GetIdentifierKey(string identifier) =>
        $"login_ratelimit:id:{identifier.Trim().ToLowerInvariant()}";
}
