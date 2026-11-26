using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// Answer to <c>GET /_synapse/admin/v2/users/{userId}/devices</c> (#598). Synapse leaves <c>display_name</c> out for a
/// device without a name. Tuwunel (1.8.2, 2.0.0) never sends <c>last_seen_user_agent</c>, and sends <c>dehydrated</c>
/// only for a user who has a dehydrated device.
/// </summary>
public class SynapseAdminUserDeviceListResult
{
    [JsonPropertyName("devices")]
    public List<SynapseAdminUserDevice> Devices { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }

    public class SynapseAdminUserDevice
    {
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("last_seen_ip")]
        public string? LastSeenIp { get; set; }

        [JsonPropertyName("last_seen_user_agent")]
        public string? LastSeenUserAgent { get; set; }

        /// <summary>Unix milliseconds.</summary>
        [JsonPropertyName("last_seen_ts")]
        public long? LastSeenTs { get; set; }

        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("dehydrated")]
        public bool? Dehydrated { get; set; }
    }
}
