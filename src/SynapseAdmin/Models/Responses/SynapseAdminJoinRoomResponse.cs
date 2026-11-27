using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>The answer to <c>POST /_synapse/admin/v1/join/{roomId}</c> (#587): the room the user joined.</summary>
public class SynapseAdminJoinRoomResponse
{
    [JsonPropertyName("room_id")]
    public string? RoomId { get; set; }
}
