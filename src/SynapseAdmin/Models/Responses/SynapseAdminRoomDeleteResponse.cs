using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// Answer to <c>DELETE /_synapse/admin/v2/rooms/{roomId}</c>: Synapse runs the deletion in the background and returns
/// the ID of that job (#518).
/// </summary>
public class SynapseAdminRoomDeleteResponse
{
    [JsonPropertyName("delete_id")]
    public string? DeleteId { get; set; }
}
