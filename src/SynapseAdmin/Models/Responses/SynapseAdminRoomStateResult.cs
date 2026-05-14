using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminRoomStateResult
{
    [JsonPropertyName("state")]
    public List<MatrixEventResponse> Events { get; set; } = [];
}
