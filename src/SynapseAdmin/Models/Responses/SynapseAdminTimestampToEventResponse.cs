using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>The answer of <c>GET /_synapse/admin/v1/rooms/{roomId}/timestamp_to_event</c> (#588).</summary>
public class SynapseAdminTimestampToEventResponse
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("origin_server_ts")]
    public long OriginServerTs { get; set; }
}
