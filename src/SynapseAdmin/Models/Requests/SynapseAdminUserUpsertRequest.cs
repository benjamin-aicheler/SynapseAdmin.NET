using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

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

    [JsonPropertyName("user_type")]
    public string? UserType { get; set; }

    [JsonPropertyName("locked")]
    public bool? Locked { get; set; }

    [JsonPropertyName("shadow_banned")]
    public bool? ShadowBanned { get; set; }
}
