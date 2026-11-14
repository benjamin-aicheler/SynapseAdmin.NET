using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

public class LoginRateLimiter : ILoginRateLimiter
{
    private const int MaxAttemptsPerWindow = 5;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);
    private readonly ILoginRateLimitStore _store;

    public LoginRateLimiter(ILoginRateLimitStore store)
    {
        _store = store;
    }

    public bool TryAcquire(string? clientIp, string? identifier = null) =>
        _store.TryAcquire(BuildKeys(clientIp, identifier), MaxAttemptsPerWindow, WindowDuration);

    /// <summary>
    /// Whether another attempt would currently be allowed, without consuming one. Test inspection only; it needs
    /// the in-memory store, because <see cref="ILoginRateLimitStore"/> deliberately has no read-only check.
    /// </summary>
    internal bool IsAllowed(string? clientIp, string? identifier = null) =>
        _store is MemoryLoginRateLimitStore memoryStore
            ? memoryStore.IsAllowed(BuildKeys(clientIp, identifier), MaxAttemptsPerWindow)
            : throw new NotSupportedException("IsAllowed is only available with the in-memory store.");

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
