using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class MatrixEventResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("state_key")]
    public string? StateKey { get; set; }

    [JsonPropertyName("content")]
    public JsonObject? RawContent { get; set; }

    [JsonPropertyName("origin_server_ts")]
    public long? OriginServerTs { get; set; }

    [JsonPropertyName("room_id")]
    public string? RoomId { get; set; }

    [JsonPropertyName("sender")]
    public string? Sender { get; set; }

    [JsonPropertyName("unsigned")]
    public JsonObject? Unsigned { get; set; }

    [JsonPropertyName("event_id")]
    public string? EventId { get; set; }
}
