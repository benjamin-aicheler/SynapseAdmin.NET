using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class SynapseAdminBackgroundUpdatesStatusResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("current_updates")]
    public Dictionary<string, CurrentBackgroundUpdate>? CurrentUpdates { get; set; }

    public class CurrentBackgroundUpdate
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("total_item_count")]
        public long TotalItemCount { get; set; }

        [JsonPropertyName("total_duration_ms")]
        public double TotalDurationMs { get; set; }

        [JsonPropertyName("average_items_per_ms")]
        public double AverageItemsPerMs { get; set; }
    }
}
