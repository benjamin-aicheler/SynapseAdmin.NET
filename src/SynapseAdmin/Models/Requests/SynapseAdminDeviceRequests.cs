using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Requests;

/// <summary>
/// The body of <c>PUT /_synapse/admin/v2/users/{userId}/devices/{deviceId}</c> (#598). Synapse and Tuwunel leave the
/// name unchanged when <c>display_name</c> is missing, so the page always sends one.
/// </summary>
public class SynapseAdminDeviceUpdateRequest
{
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }
}

/// <summary>
/// The body of <c>POST /_synapse/admin/v2/users/{userId}/delete_devices</c> (#598): the devices to delete, which
/// invalidates their access tokens.
/// </summary>
public class SynapseAdminDeleteDevicesRequest
{
    [JsonPropertyName("devices")]
    public required IReadOnlyList<string> Devices { get; init; }
}
