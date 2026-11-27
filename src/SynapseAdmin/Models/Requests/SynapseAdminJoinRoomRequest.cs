using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>The body of <c>POST /_synapse/admin/v1/join/{roomId}</c> (#587).</summary>
public class SynapseAdminJoinRoomRequest
{
    /// <summary>The local user to join to the room.</summary>
    [JsonPropertyName("user_id")]
    public required string UserId { get; init; }
}
