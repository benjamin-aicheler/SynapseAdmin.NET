using System.Text.Json.Serialization;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminMediaMetadataResponse
{
    [JsonPropertyName("media_info")]
    public SynapseAdminUserMediaResult.MediaInfo? MediaInfo { get; set; }
}