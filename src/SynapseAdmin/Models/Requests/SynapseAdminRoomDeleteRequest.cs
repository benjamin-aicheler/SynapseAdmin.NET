using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// The body of <c>DELETE /_synapse/admin/v2/rooms/{roomId}</c>. The Admin API documents <c>new_room_user_id</c>,
/// <c>room_name</c> and <c>message</c> as optional strings, so a null one is left out instead of sent as <c>null</c>
/// (#543). <c>block</c> and <c>purge</c> are always sent, which keeps the body from being empty.
/// </summary>
public class SynapseAdminRoomDeleteRequest
{
    [JsonPropertyName("new_room_user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NewRoomUserId { get; set; }

    [JsonPropertyName("room_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RoomName { get; set; }

    [JsonPropertyName("block")]
    public bool Block { get; set; }

    [JsonPropertyName("purge")]
    public bool Purge { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}
