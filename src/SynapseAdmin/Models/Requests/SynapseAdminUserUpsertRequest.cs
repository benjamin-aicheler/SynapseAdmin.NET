using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// The body of <c>PUT /_synapse/admin/v2/users/{userId}</c>, which creates the user or modifies an existing account.
/// Declare only the parameters that endpoint documents (#546). <c>MatrixHttpClient</c> omits every <c>null</c> field,
/// and the endpoint reads an omitted field as "unchanged".
/// </summary>
public class SynapseAdminUserUpsertRequest
{
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("displayname")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("admin")]
    public bool? Admin { get; set; }

    [JsonPropertyName("deactivated")]
    public bool? Deactivated { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// <c>null</c> is omitted and leaves the type unchanged, so this request can't clear it: the endpoint clears the type
    /// only for an explicit <c>null</c>. To add that, represent omit, set and clear separately and assert all three on
    /// the wire, as <c>SynapseAdminRegistrationTokenUpdateRequest</c> does (#519). Never send <c>null</c> on every
    /// request, which would clear the type of an existing account.
    /// </summary>
    [JsonPropertyName("user_type")]
    public string? UserType { get; set; }

    [JsonPropertyName("locked")]
    public bool? Locked { get; set; }
}
