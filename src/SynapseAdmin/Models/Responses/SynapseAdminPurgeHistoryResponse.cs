using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminPurgeHistoryResponse
{
    [JsonPropertyName("purge_id")]
    public string PurgeId { get; set; } = string.Empty;
}
