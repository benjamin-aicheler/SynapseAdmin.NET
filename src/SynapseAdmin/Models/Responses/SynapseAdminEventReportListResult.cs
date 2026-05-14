using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminEventReportListResult
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("next_token")]
    public string? NextToken { get; set; }

    [JsonPropertyName("event_reports")]
    public List<SynapseAdminEventReportListResultReport> Reports { get; set; } = [];

    public class SynapseAdminEventReportListResultReport
    {
        [JsonPropertyName("event_id")]
        public string EventId { get; set; } = null!;

        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("score")]
        public int? Score { get; set; }

        [JsonPropertyName("received_ts")]
        public long ReceivedTs { get; set; }

        [JsonPropertyName("canonical_alias")]
        public string? CanonicalAlias { get; set; }

        [JsonPropertyName("room_id")]
        public string RoomId { get; set; } = null!;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("sender")]
        public string Sender { get; set; } = null!;

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;
    }
}
