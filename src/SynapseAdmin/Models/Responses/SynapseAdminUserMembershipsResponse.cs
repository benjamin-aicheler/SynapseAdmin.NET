using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminUserMembershipsResponse
{
    [JsonPropertyName("memberships")]
    public Dictionary<string, string> Memberships { get; set; } = [];
}
