using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

/// <summary>
/// Short-lived, single-use, browser-bound handoff of session data from a Blazor circuit to the
/// cookie sign-in endpoint (see <c>AuthController.SignIn</c>).
/// </summary>
public class SessionBridgeService(
    IMemoryCache cache,
    ILogger<SessionBridgeService> logger,
    TimeProvider? timeProvider = null) : ISessionBridgeService
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private const string CacheKeyPrefix = "session_bridge:";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private sealed class BridgeEntry(
        (string Homeserver, string AccessToken, string UserId, string Username) data,
        string bindingToken,
        DateTimeOffset expiresAtUtc)
    {
        public (string Homeserver, string AccessToken, string UserId, string Username) Data { get; } = data;
        public string BindingToken { get; } = bindingToken;
        public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;

        // 0 = available, 1 = consumed. Claimed with Interlocked so exactly one caller can redeem the entry.
        public int Consumed;
    }

    public string CreateBridge(string homeserver, string accessToken, string userId, string username, string bindingToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(bindingToken);

        var key = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = _time.GetUtcNow().Add(Lifetime);

        logger.LogInformation("Creating session bridge for user {UserId} on {Homeserver}",
            userId.SanitizeForLogging(), homeserver.SanitizeForLogging());

        cache.Set(CacheKeyPrefix + key,
            new BridgeEntry((homeserver, accessToken, userId, username), bindingToken, expiresAt),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
        return key;
    }

    public bool TryConsumeBridge(
        string? key,
        string? bindingToken,
        out (string Homeserver, string AccessToken, string UserId, string Username) data)
    {
        data = (string.Empty, string.Empty, string.Empty, string.Empty);

        if (string.IsNullOrEmpty(key) ||
            !cache.TryGetValue(CacheKeyPrefix + key, out BridgeEntry? entry) ||
            entry is null)
        {
            logger.LogWarning("Failed to consume session bridge - key not found or expired");
            return false;
        }

        if (_time.GetUtcNow() >= entry.ExpiresAtUtc)
        {
            cache.Remove(CacheKeyPrefix + key);
            logger.LogWarning("Failed to consume session bridge - key expired");
            return false;
        }

        // Binding mismatch does not consume the key: a party without the initiating browser's
        // antiforgery token cannot redeem it, and must not be able to burn it for the legitimate browser either.
        if (!BindingMatches(entry.BindingToken, bindingToken))
        {
            logger.LogWarning("Failed to consume session bridge - request is not bound to the initiating browser");
            return false;
        }

        if (Interlocked.Exchange(ref entry.Consumed, 1) != 0)
        {
            logger.LogWarning("Failed to consume session bridge - key was already used");
            return false;
        }

        cache.Remove(CacheKeyPrefix + key);
        logger.LogInformation("Successfully consumed session bridge for user {UserId}",
            entry.Data.UserId.SanitizeForLogging());
        data = entry.Data;
        return true;
    }

    private static bool BindingMatches(string expected, string? actual) =>
        !string.IsNullOrEmpty(actual) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
