using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SendServerNoticeResponse
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;
}
