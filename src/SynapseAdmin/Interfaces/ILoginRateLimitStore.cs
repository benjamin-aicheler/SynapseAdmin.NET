namespace SynapseAdmin.Interfaces;

/// <summary>
/// Backing store for login rate-limiting counters.
/// Replace with a distributed implementation (e.g., Redis) in multi-replica deployments.
/// </summary>
public interface ILoginRateLimitStore
{
    /// <summary>
    /// Atomically checks whether all specified keys are below <paramref name="maxAttempts"/>
    /// and increments every key within the same atomic operation if allowed.
    /// </summary>
    bool TryAcquire(IReadOnlyList<string> keys, int maxAttempts, TimeSpan windowDuration);

    /// <summary>
    /// Clears rate-limit counters for all specified keys.
    /// </summary>
    void Reset(IReadOnlyList<string> keys);
}

