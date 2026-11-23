using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// The status of one room deletion job, from <c>/_synapse/admin/v2/rooms/delete_status/{deleteId}</c> or as an entry of
/// <c>/_synapse/admin/v2/rooms/{roomId}/delete_status</c> (#518). <c>shutdown_room</c> isn't read.
/// </summary>
public class SynapseAdminRoomDeleteStatusResponse
{
    [JsonPropertyName("delete_id")]
    public string? DeleteId { get; set; }

    [JsonPropertyName("room_id")]
    public string? RoomId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // scheduled, active, complete, failed

    /// <summary>Synapse's error text, only when <see cref="Status"/> is <c>failed</c>. For the log, never the UI.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
