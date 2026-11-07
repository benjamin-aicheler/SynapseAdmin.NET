using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

public class SynapseAdminAccountValidityRequest
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("expiration_ts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpirationTs { get; set; }

    [JsonPropertyName("enable_renewal_emails")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EnableRenewalEmails { get; set; }
}

