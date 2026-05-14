using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

public class SynapseAdminRegistrationTokenUpdateRequest
{
    [JsonPropertyName("uses_allowed")]
    public int? UsesAllowed { get; set; }

    [JsonPropertyName("expiry_time")]
    public long? ExpiryTime { get; set; }
}

public class SynapseAdminRegistrationTokenCreateRequest : SynapseAdminRegistrationTokenUpdateRequest
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("length")]
    public int? Length { get; set; }
}
