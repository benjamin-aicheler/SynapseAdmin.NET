using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminRoomMediaListResult
{
    [JsonPropertyName("local")]
    public List<string> Local { get; set; } = [];

    [JsonPropertyName("remote")]
    public List<string> Remote { get; set; } = [];
}
