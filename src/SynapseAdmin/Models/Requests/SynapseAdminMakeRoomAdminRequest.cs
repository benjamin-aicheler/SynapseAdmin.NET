using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// The body of <c>POST /_synapse/admin/v1/rooms/{roomId}/make_room_admin</c> (#587). <see cref="UserId"/> is left out
/// when <c>null</c> (<c>PostAsJsonAsync</c> drops nulls), so the body is <c>{}</c> and Synapse grants the power to the
/// calling admin, its documented default.
/// </summary>
public class SynapseAdminMakeRoomAdminRequest
{
    /// <summary>The user to grant the power to; any server, as Synapse doesn't check it.</summary>
    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }
}
