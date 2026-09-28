using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// Response model for the Synapse Admin Fetch Event API (GET /_synapse/admin/v1/fetch_event/<event_id>).
/// </summary>
public class SynapseAdminFetchEventResponse
{
    [JsonPropertyName("event")]
    public JsonObject? Event { get; set; }
}
