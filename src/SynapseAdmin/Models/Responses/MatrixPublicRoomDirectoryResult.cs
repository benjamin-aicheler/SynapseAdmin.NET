using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class MatrixPublicRoomDirectoryResult
{
    [JsonPropertyName("chunk")]
    public List<PublicRoomListItem> Chunk { get; set; } = [];

    [JsonPropertyName("next_batch")]
    public string? NextBatch { get; set; }

    [JsonPropertyName("prev_batch")]
    public string? PrevBatch { get; set; }

    [JsonPropertyName("total_room_count_estimate")]
    public int? TotalRoomCountEstimate { get; set; }

    public class PublicRoomListItem
    {
        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }

        [JsonPropertyName("canonical_alias")]
        public string? CanonicalAlias { get; set; }

        [JsonPropertyName("guest_can_join")]
        public bool GuestCanJoin { get; set; }

        [JsonPropertyName("join_rule")]
        public string? JoinRule { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("num_joined_members")]
        public int NumJoinedMembers { get; set; }

        [JsonPropertyName("room_id")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("topic")]
        public string? Topic { get; set; }

        [JsonPropertyName("world_readable")]
        public bool WorldReadable { get; set; }
    }
}
