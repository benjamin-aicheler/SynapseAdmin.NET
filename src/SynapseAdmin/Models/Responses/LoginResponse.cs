using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class LoginResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = null!;

    [JsonPropertyName("home_server")]
    public string Homeserver { get; set; } = null!;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = null!;
}
