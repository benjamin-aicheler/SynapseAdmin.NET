using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// The Event Context API's answer read as room messages, for jumping to a date (#588): the same request as
/// <see cref="SynapseAdminEventContextResponse"/>, with the events typed as the messages API's. <see cref="EventsBefore"/>
/// is newest first, <see cref="EventsAfter"/> oldest first. <see cref="Start"/> pages older (<c>dir=b</c>) and
/// <see cref="End"/> newer (<c>dir=f</c>) through the messages API. Never log the events.
/// </summary>
public class SynapseAdminRoomMessageContextResponse
{
    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("events_before")]
    public List<SynapseAdminEvent> EventsBefore { get; set; } = [];

    [JsonPropertyName("event")]
    public SynapseAdminEvent? Event { get; set; }

    [JsonPropertyName("events_after")]
    public List<SynapseAdminEvent> EventsAfter { get; set; } = [];
}
