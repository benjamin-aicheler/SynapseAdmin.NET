using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class LoginResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("home_server")]
    public string? Homeserver { get; set; }

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = null!;
}
