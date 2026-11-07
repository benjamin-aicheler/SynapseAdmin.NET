using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminAccountValidityResponse
{
    [JsonPropertyName("expiration_ts")]
    public long ExpirationTs { get; set; }
}

