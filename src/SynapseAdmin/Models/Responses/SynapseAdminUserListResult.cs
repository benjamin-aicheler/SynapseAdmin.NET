using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminUserListResult
{
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("next_token")]
    public string? NextToken { get; set; }

    [JsonPropertyName("users")]
    public List<SynapseAdminUserListResultUser> Users { get; set; } = [];

    public class SynapseAdminUserListResultUser
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("is_guest")]
        public bool? IsGuest { get; set; }

        [JsonPropertyName("admin")]
        public bool? Admin { get; set; }

        [JsonPropertyName("user_type")]
        public string? UserType { get; set; }

        [JsonPropertyName("deactivated")]
        public bool Deactivated { get; set; }

        [JsonPropertyName("erased")]
        public bool Erased { get; set; }

        [JsonPropertyName("shadow_banned")]
        public bool ShadowBanned { get; set; }

        [JsonPropertyName("displayname")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }

        [JsonPropertyName("creation_ts")]
        public long CreationTs { get; set; }

        [JsonPropertyName("last_seen_ts")]
        public long? LastSeenTs { get; set; }

        [JsonPropertyName("locked")]
        public bool Locked { get; set; }

        [JsonPropertyName("approved")]
        public bool? Approved { get; set; }

        [JsonPropertyName("suspended")]
        public bool? Suspended { get; set; }

        [JsonPropertyName("appservice_id")]
        public string? AppserviceId { get; set; }

        [JsonPropertyName("consent_version")]
        public string? ConsentVersion { get; set; }

        [JsonPropertyName("consent_server_notice_sent")]
        public string? ConsentServerNoticeSent { get; set; }
    }
}
