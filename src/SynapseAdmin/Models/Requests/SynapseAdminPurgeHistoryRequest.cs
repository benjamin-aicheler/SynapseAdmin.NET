using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

public class SynapseAdminPurgeHistoryRequest
{
    [JsonPropertyName("delete_local_events")]
    public bool? DeleteLocalEvents { get; set; }

    [JsonPropertyName("purge_up_to_event_id")]
    public string? PurgeUpToEventId { get; set; }

    [JsonPropertyName("purge_up_to_ts")]
    public long? PurgeUpToTs { get; set; }
}
