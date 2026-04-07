using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseVersionResponse
{
    [JsonPropertyName("server_version")]
    public string ServerVersion { get; set; } = string.Empty;
}
