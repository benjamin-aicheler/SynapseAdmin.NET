using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class UserMediaStatisticsResponse
{
    [JsonPropertyName("users")]
    public List<UserMediaStatistic> Users { get; set; } = [];

    [JsonPropertyName("next_token")]
    public int? NextToken { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    public class UserMediaStatistic
    {
        [JsonPropertyName("displayname")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("media_count")]
        public int MediaCount { get; set; }

        [JsonPropertyName("media_length")]
        public long MediaLength { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;
    }
}
