using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// Answer to <c>GET /_synapse/admin/v1/whois/{userId}</c> (#598): connections grouped by device and session. Synapse
/// and Tuwunel put every connection under the empty device key. Synapse lists the user's recorded IP and user agent
/// combinations; Tuwunel (1.8.2, 2.0.0) lists each device's last IP and time once, without a user agent.
/// </summary>
public class SynapseAdminWhoisResponse
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("devices")]
    public Dictionary<string, WhoisDevice>? Devices { get; set; }

    public class WhoisDevice
    {
        [JsonPropertyName("sessions")]
        public List<WhoisSession>? Sessions { get; set; }
    }

    public class WhoisSession
    {
        [JsonPropertyName("connections")]
        public List<WhoisConnection>? Connections { get; set; }
    }

    public class WhoisConnection
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        /// <summary>Unix milliseconds.</summary>
        [JsonPropertyName("last_seen")]
        public long? LastSeen { get; set; }

        [JsonPropertyName("user_agent")]
        public string? UserAgent { get; set; }
    }
}
