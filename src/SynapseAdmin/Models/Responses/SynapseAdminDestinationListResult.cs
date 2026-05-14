using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminDestinationListResult
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("next_token")]
    public string? NextToken { get; set; }

    [JsonPropertyName("destinations")]
    public List<SynapseAdminDestinationListResultDestination> Destinations { get; set; } = [];

    public class SynapseAdminDestinationListResultDestination
    {
        [JsonPropertyName("destination")]
        public string Destination { get; set; } = null!;

        [JsonPropertyName("retry_last_ts")]
        public long RetryLastTs { get; set; }

        [JsonPropertyName("retry_interval")]
        public long RetryInterval { get; set; }

        [JsonPropertyName("failure_ts")]
        public long? FailureTs { get; set; }

        [JsonPropertyName("last_successful_stream_ordering")]
        public long? LastSuccessfulStreamOrdering { get; set; }
    }
}
