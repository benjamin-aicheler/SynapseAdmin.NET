using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// Answer to <c>GET /_synapse/admin/v1/rooms/{roomId}/block</c> (#586): whether the room is blocked and, if it is, the
/// user who blocked it. Synapse and Tuwunel leave <c>user_id</c> out when the room isn't blocked, and Tuwunel also for a
/// room blocked before it recorded the user.
/// </summary>
public class SynapseAdminRoomBlockStatusResponse
{
    [JsonPropertyName("block")]
    public bool Block { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }
}
