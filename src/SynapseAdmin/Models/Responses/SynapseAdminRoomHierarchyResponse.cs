using System.Text.Json;
using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// The answer of <c>GET /_synapse/admin/v1/rooms/{roomId}/hierarchy</c> (#588). The first page starts with the requested
/// room's own entry, whose <see cref="SynapseAdminRoomHierarchyRoom.ChildrenState"/> names its children. Rooms this
/// server isn't in have no entry. <see cref="NextBatch"/> is opaque and valid only with the same room and
/// <c>max_depth</c>.
/// </summary>
public class SynapseAdminRoomHierarchyResponse
{
    [JsonPropertyName("rooms")]
    public List<SynapseAdminRoomHierarchyRoom> Rooms { get; set; } = [];

    [JsonPropertyName("next_batch")]
    public string? NextBatch { get; set; }
}

/// <summary>A room's summary in the space hierarchy, as the Client-Server API's hierarchy chunk.</summary>
public class SynapseAdminRoomHierarchyRoom
{
    [JsonPropertyName("room_id")]
    public string RoomId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("canonical_alias")]
    public string? CanonicalAlias { get; set; }

    [JsonPropertyName("num_joined_members")]
    public int? NumJoinedMembers { get; set; }

    [JsonPropertyName("join_rule")]
    public string? JoinRule { get; set; }

    [JsonPropertyName("room_type")]
    public string? RoomType { get; set; }

    [JsonPropertyName("children_state")]
    public List<SynapseAdminSpaceChildEvent> ChildrenState { get; set; } = [];
}

/// <summary>An <c>m.space.child</c> state event of a space: <see cref="StateKey"/> is the child's room ID.</summary>
public class SynapseAdminSpaceChildEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("state_key")]
    public string StateKey { get; set; } = string.Empty;

    /// <summary><c>via</c> (the servers to join through, required for a valid link) and the optional <c>suggested</c>.</summary>
    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }
}
