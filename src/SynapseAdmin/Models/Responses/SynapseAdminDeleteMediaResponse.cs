using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminDeleteMediaResponse
{
    [JsonPropertyName("deleted_media")]
    public List<string> DeletedMedia { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }
}
