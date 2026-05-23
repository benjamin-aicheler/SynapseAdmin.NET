using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminBackgroundUpdatesEnabledResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}
