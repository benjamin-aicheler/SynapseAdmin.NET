using System.Text.Json.Serialization;

namespace SynapseAdmin.Models.Responses;

public class RoomStatisticsResponse
{
    [JsonPropertyName("rooms")]
    public List<RoomStatistic> Rooms { get; set; } = [];

    public class RoomStatistic
    {
        [JsonPropertyName("room_id")]
        public string RoomId { get; set; } = string.Empty;

        [JsonPropertyName("estimated_size")]
        public long EstimatedSize { get; set; }
    }
}
