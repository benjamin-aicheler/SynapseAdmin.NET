using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminPurgeMediaCacheResponse
{
    [JsonPropertyName("deleted")]
    public int Deleted { get; set; }
}
