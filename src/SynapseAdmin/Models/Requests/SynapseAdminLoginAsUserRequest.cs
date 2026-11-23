using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// Body of <c>POST /_synapse/admin/v1/users/{userId}/login</c>. Synapse reads the expiry only from the body, as an
/// integer Unix timestamp in milliseconds, and issues a token that never expires without it (#539).
/// </summary>
public class SynapseAdminLoginAsUserRequest
{
    [JsonPropertyName("valid_until_ms")]
    public long ValidUntilMs { get; set; }
}
