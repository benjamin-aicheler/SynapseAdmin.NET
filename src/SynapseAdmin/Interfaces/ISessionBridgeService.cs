namespace SynapseAdmin.Interfaces;

public interface ISessionBridgeService
{
    string CreateBridge(string homeserver, string accessToken, string userId, string username);
    bool TryConsumeBridge(string key, out (string Homeserver, string AccessToken, string UserId, string Username) data);
}
