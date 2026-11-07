using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

public class LoginRateLimiter : ILoginRateLimiter
{
    private const int MaxAttemptsPerWindow = 5;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);
    private readonly ILoginRateLimitStore _store;

    [ActivatorUtilitiesConstructor]
    public LoginRateLimiter(ILoginRateLimitStore store)
    {
        _store = store;
    }

    public LoginRateLimiter(IMemoryCache cache)
        : this(new MemoryLoginRateLimitStore(cache))
    {
    }

    public bool TryAcquire(string? clientIp, string? identifier = null) =>
        _store.TryAcquire(BuildKeys(clientIp, identifier), MaxAttemptsPerWindow, WindowDuration);

    public bool IsAllowed(string? clientIp, string? identifier = null) =>
        _store.IsAllowed(BuildKeys(clientIp, identifier), MaxAttemptsPerWindow);

    public void RecordFailedAttempt(string? clientIp, string? identifier = null) =>
        _store.Increment(BuildKeys(clientIp, identifier), WindowDuration);

    public void ResetAttempts(string? clientIp, string? identifier = null) =>
        _store.Reset(BuildKeys(clientIp, identifier));

    private static IReadOnlyList<string> BuildKeys(string? clientIp, string? identifier)
    {
        var ipKey = GetIpKey(clientIp);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return [ipKey];
        }

        return [ipKey, GetIdentifierKey(identifier)];
    }

    private static string GetIpKey(string? clientIp) =>
        $"login_ratelimit:ip:{(string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim())}";

    private static string GetIdentifierKey(string identifier) =>
        $"login_ratelimit:id:{identifier.Trim().ToLowerInvariant()}";
}
