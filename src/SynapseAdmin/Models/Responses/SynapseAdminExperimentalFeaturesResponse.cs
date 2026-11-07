using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminExperimentalFeaturesResponse
{
    [JsonPropertyName("features")]
    public Dictionary<string, bool> Features { get; set; } = [];
}

