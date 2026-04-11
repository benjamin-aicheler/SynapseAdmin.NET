namespace SynapseAdmin.Interfaces;

public interface ISessionBridgeService
{
    string CreateBridge(string homeserver, string accessToken, string userId);
    bool TryConsumeBridge(string key, out (string Homeserver, string AccessToken, string UserId) data);
}
