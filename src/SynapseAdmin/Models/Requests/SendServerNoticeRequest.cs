using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

public class SendServerNoticeRequest
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object Content { get; set; } = new();

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("state_key")]
    public string? StateKey { get; set; }
}
