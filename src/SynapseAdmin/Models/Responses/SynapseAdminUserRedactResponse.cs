using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>The answer to <c>POST /_synapse/admin/v1/user/{userId}/redact</c> (#600).</summary>
public class SynapseAdminUserRedactResponse
{
    [JsonPropertyName("redact_id")]
    public string? RedactId { get; set; }
}
