using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

public class SynapseAdminRoomDeleteRequest
{
    [JsonPropertyName("new_room_user_id")]
    public string? NewRoomUserId { get; set; }

    [JsonPropertyName("room_name")]
    public string? RoomName { get; set; }

    [JsonPropertyName("block")]
    public bool Block { get; set; }

    [JsonPropertyName("purge")]
    public bool Purge { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
