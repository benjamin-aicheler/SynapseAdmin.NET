using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

/// <summary>The status of a redaction of a user's events, from <c>/_synapse/admin/v1/user/redact_status/{redactId}</c> (#600).</summary>
public class SynapseAdminUserRedactStatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // scheduled, active, complete, failed

    /// <summary>
    /// When <see cref="Status"/> is <c>complete</c>: the events that couldn't be redacted, by event ID, with Synapse's
    /// error text. For the log, never the UI.
    /// </summary>
    [JsonPropertyName("failed_redactions")]
    public Dictionary<string, string>? FailedRedactions { get; set; }

    /// <summary>Synapse's error text, only when <see cref="Status"/> is <c>failed</c>. For the log, never the UI.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
