using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminRegistrationTokenListResult
{
    [JsonPropertyName("registration_tokens")]
    public List<SynapseAdminRegistrationTokenListResultToken> RegistrationTokens { get; set; } = [];

    public class SynapseAdminRegistrationTokenListResultToken
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = null!;

        [JsonPropertyName("uses_allowed")]
        public int? UsesAllowed { get; set; }

        [JsonPropertyName("pending")]
        public int Pending { get; set; }

        [JsonPropertyName("completed")]
        public int Completed { get; set; }

        [JsonPropertyName("expiry_time")]
        public long? ExpiryTime { get; set; }
    }
}
