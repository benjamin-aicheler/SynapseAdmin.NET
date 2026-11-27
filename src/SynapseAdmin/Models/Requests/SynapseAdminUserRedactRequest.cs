using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// The body of <c>POST /_synapse/admin/v1/user/{userId}/redact</c> (#600). <see cref="Rooms"/> is required and always
/// sent: an empty list means the rooms the user is joined to or banned from. <see cref="Reason"/> is left out when
/// <c>null</c> (<c>PostAsJsonAsync</c> drops nulls). There is deliberately no <c>after_ts</c> or <c>before_ts</c>: a
/// Synapse older than 1.157 ignores them and would redact events of any age (SPEC.md §10).
/// </summary>
public class SynapseAdminUserRedactRequest
{
    [JsonPropertyName("rooms")]
    public required IReadOnlyList<string> Rooms { get; init; }

    /// <summary>Included in each redaction event, so room members see it.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>How many of the user's events to search per room, newest first.</summary>
    [JsonPropertyName("limit")]
    public required int Limit { get; init; }

    /// <summary>Whether the admin sends the redactions instead of the user.</summary>
    [JsonPropertyName("use_admin")]
    public required bool UseAdmin { get; init; }
}
