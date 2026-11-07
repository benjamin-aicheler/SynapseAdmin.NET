using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

public class SynapseAdminExperimentalFeaturesRequest
{
    [JsonPropertyName("features")]
    public Dictionary<string, bool> Features { get; set; } = [];
}

