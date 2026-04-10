using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminRoomMessagesResponse
{
    [JsonPropertyName("chunk")]
    public List<SynapseAdminEvent> Chunk { get; set; } = [];

    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("state")]
    public List<SynapseAdminEvent> State { get; set; } = [];
}

public class SynapseAdminEvent
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    [JsonPropertyName("origin_server_ts")]
    public long OriginServerTs { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("state_key")]
    public string? StateKey { get; set; }

    [JsonPropertyName("content")]
    public object? Content { get; set; }

    [JsonPropertyName("unsigned")]
    public object? Unsigned { get; set; }
}
