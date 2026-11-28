using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// The answer of <c>GET /_synapse/admin/v1/event_reports/{reportId}</c> (#588): the list entry's fields and the reported
/// event as stored. <c>id</c> is an integer, so the gateway reads it with the compatibility options.
/// </summary>
public class SynapseAdminEventReportDetailsResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("received_ts")]
    public long ReceivedTs { get; set; }

    [JsonPropertyName("room_id")]
    public string RoomId { get; set; } = null!;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("canonical_alias")]
    public string? CanonicalAlias { get; set; }

    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = null!;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = null!;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("score")]
    public int? Score { get; set; }

    [JsonPropertyName("sender")]
    public string? Sender { get; set; }

    /// <summary>The reported event as Synapse stores it. Never log it.</summary>
    [JsonPropertyName("event_json")]
    public JsonObject? EventJson { get; set; }
}
