using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// Body of <c>PUT /_synapse/admin/v1/registration_tokens/&lt;token&gt;</c>. Synapse leaves an omitted field unchanged
/// and treats an explicit <c>null</c> as "unlimited uses" or "never expires", so both fields are always sent, even
/// though LibMatrix's <c>MatrixHttpClient</c> otherwise omits null properties (#519).
/// </summary>
public class SynapseAdminRegistrationTokenUpdateRequest
{
    [JsonPropertyName("uses_allowed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? UsesAllowed { get; set; }

    [JsonPropertyName("expiry_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? ExpiryTime { get; set; }
}

/// <summary>
/// Body of <c>POST /_synapse/admin/v1/registration_tokens/new</c>. Null fields are omitted, so Synapse's defaults
/// apply (a generated token, unlimited uses, no expiry). Deliberately not derived from
/// <see cref="SynapseAdminRegistrationTokenUpdateRequest"/>, whose fields are always sent.
/// </summary>
public class SynapseAdminRegistrationTokenCreateRequest
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("length")]
    public int? Length { get; set; }

    [JsonPropertyName("uses_allowed")]
    public int? UsesAllowed { get; set; }

    [JsonPropertyName("expiry_time")]
    public long? ExpiryTime { get; set; }
}
