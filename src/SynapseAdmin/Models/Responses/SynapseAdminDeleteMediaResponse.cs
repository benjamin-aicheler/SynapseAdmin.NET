using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminDeleteMediaResponse
{
    [JsonPropertyName("deleted_media")]
    public List<string> DeletedMedia { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }
}
