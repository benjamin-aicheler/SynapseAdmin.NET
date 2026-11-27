using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// The body of the Client-Server <c>POST /_matrix/client/v3/rooms/{roomId}/invite</c>, which the Tuwunel gateway sends
/// before an admin join, as Synapse invites before it joins (#587).
/// </summary>
public class MatrixRoomInviteRequest
{
    [JsonPropertyName("user_id")]
    public required string UserId { get; init; }
}
