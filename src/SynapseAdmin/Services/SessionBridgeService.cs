using Microsoft.Extensions.Caching.Memory;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

public class SessionBridgeService(IMemoryCache cache) : ISessionBridgeService
{
    public string CreateBridge(string homeserver, string accessToken, string userId)
    {
        var key = Guid.NewGuid().ToString();
        var data = (homeserver, accessToken, userId);
        
        cache.Set(key, data, TimeSpan.FromSeconds(60));
        return key;
    }

    public bool TryConsumeBridge(string key, out (string Homeserver, string AccessToken, string UserId) data)
    {
        if (cache.TryGetValue(key, out (string, string, string) cachedData))
        {
            data = cachedData;
            cache.Remove(key); // One-time use only
            return true;
        }

        data = (string.Empty, string.Empty, string.Empty);
        return false;
    }
}
