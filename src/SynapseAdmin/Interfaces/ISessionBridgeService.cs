namespace SynapseAdmin.Interfaces;

public interface ISessionBridgeService
{
    /// <summary>
    /// Creates a single-use bridge key (valid for 60 seconds) for the given session data.
    /// <paramref name="bindingToken"/> is the initiating browser's antiforgery request token; the key can only be
    /// redeemed by a request that presents the same, valid token.
    /// </summary>
    string CreateBridge(string homeserver, string accessToken, string userId, string username, string bindingToken);

    /// <summary>
    /// Atomically redeems a bridge key. Succeeds at most once per key, only before expiry, and only when
    /// <paramref name="bindingToken"/> matches the token the bridge was created with.
    /// </summary>
    bool TryConsumeBridge(string? key, string? bindingToken, out (string Homeserver, string AccessToken, string UserId, string Username) data);
}
