using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// The body of <c>POST /_synapse/admin/v1/reset_password/{userId}</c>. Both fields are always sent: Synapse defaults
/// <c>logout_devices</c> to <c>true</c>, while the page defaults it to <c>false</c> (#584).
/// </summary>
public class SynapseAdminResetPasswordRequest
{
    [JsonPropertyName("new_password")]
    public required string NewPassword { get; init; }

    [JsonPropertyName("logout_devices")]
    public required bool LogoutDevices { get; init; }
}
