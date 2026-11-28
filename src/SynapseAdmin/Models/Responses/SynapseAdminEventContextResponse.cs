using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// The answer of <c>GET /_synapse/admin/v1/rooms/{roomId}/context/{eventId}</c> (#588), the admin form of the
/// Client-Server <c>/context</c>. <see cref="EventsBefore"/> is newest first, <see cref="EventsAfter"/> oldest first.
/// <c>state</c> isn't read. <see cref="Start"/> and <see cref="End"/> are opaque tokens for the room's messages API.
/// Never log the events.
/// </summary>
public class SynapseAdminEventContextResponse
{
    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("events_before")]
    public List<JsonObject> EventsBefore { get; set; } = [];

    [JsonPropertyName("event")]
    public JsonObject? Event { get; set; }

    [JsonPropertyName("events_after")]
    public List<JsonObject> EventsAfter { get; set; } = [];
}
