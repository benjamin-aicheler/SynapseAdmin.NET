using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminPurgeHistoryStatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // active, complete, failed

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
