using Microsoft.Extensions.Caching.Memory;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

public class MemoryLoginRateLimitStore(IMemoryCache cache) : ILoginRateLimitStore
{
    private readonly Lock _syncLock = new();

    private sealed record RateLimitBucket(int Count, DateTimeOffset ExpiresAtUtc);

    public bool TryAcquire(IReadOnlyList<string> keys, int maxAttempts, TimeSpan windowDuration)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_syncLock)
        {
            foreach (var key in keys)
            {
                if (GetActiveBucket(key, now)?.Count >= maxAttempts)
                {
                    return false;
                }
            }

            foreach (var key in keys)
            {
                IncrementBucketCore(key, now, windowDuration);
            }

            return true;
        }
    }

    /// <summary>Checks the counters without incrementing them. Test inspection only; not part of the store contract.</summary>
    internal bool IsAllowed(IReadOnlyList<string> keys, int maxAttempts)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_syncLock)
        {
            foreach (var key in keys)
            {
                if (GetActiveBucket(key, now)?.Count >= maxAttempts)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void Reset(IReadOnlyList<string> keys)
    {
        lock (_syncLock)
        {
            foreach (var key in keys)
            {
                cache.Remove(key);
            }
        }
    }

    private RateLimitBucket? GetActiveBucket(string key, DateTimeOffset now)
    {
        if (cache.TryGetValue(key, out RateLimitBucket? bucket) &&
            bucket is not null &&
            bucket.ExpiresAtUtc > now)
        {
            return bucket;
        }

        return null;
    }

    private void IncrementBucketCore(string key, DateTimeOffset now, TimeSpan windowDuration)
    {
        var existing = GetActiveBucket(key, now);
        var expiresAt = existing?.ExpiresAtUtc ?? now.Add(windowDuration);
        var updated = new RateLimitBucket((existing?.Count ?? 0) + 1, expiresAt);

        cache.Set(key, updated, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = expiresAt
        });
    }
}

