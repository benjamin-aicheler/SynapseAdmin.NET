using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

public class SessionBridgeService(IMemoryCache cache, ILogger<SessionBridgeService> logger) : ISessionBridgeService
{
    public string CreateBridge(string homeserver, string accessToken, string userId, string username)
    {
        var key = Guid.NewGuid().ToString();
        var data = (homeserver, accessToken, userId, username);
        
        logger.LogInformation("Creating session bridge for user {UserId} on {Homeserver}", 
            userId.SanitizeForLogging(), homeserver.SanitizeForLogging());
        
        cache.Set(key, data, TimeSpan.FromSeconds(60));
        return key;
    }

    public bool TryConsumeBridge(string key, out (string Homeserver, string AccessToken, string UserId, string Username) data)
    {
        if (cache.TryGetValue(key, out (string, string, string, string) cachedData))
        {
            logger.LogInformation("Successfully consumed session bridge for user {UserId}", 
                cachedData.Item3.SanitizeForLogging());
            data = cachedData;
            cache.Remove(key); // One-time use only
            return true;
        }

        logger.LogWarning("Failed to consume session bridge - key not found or expired");
        data = (string.Empty, string.Empty, string.Empty, string.Empty);
        return false;
    }
}
