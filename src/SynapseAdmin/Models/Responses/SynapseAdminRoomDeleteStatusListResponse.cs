using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>
/// Answer to <c>/_synapse/admin/v2/rooms/{roomId}/delete_status</c>: the room's active deletions and those finished in the
/// last 24 hours (#518).
/// </summary>
public class SynapseAdminRoomDeleteStatusListResponse
{
    [JsonPropertyName("results")]
    public List<SynapseAdminRoomDeleteStatusResponse> Results { get; set; } = [];
}
