using System.Text.Json.Nodes;

namespace SynapseAdmin.Models.ViewModels;

public class EventForensicsViewModel
{
    public string EventId { get; set; } = string.Empty;
    public string? RoomId { get; set; }
    public string? Sender { get; set; }
    public string? Type { get; set; }
    public long? OriginServerTs { get; set; }
    public DateTimeOffset? Timestamp => OriginServerTs.HasValue 
        ? DateTimeOffset.FromUnixTimeMilliseconds(OriginServerTs.Value) 
        : null;
    public JsonObject? Content { get; set; }
    public JsonObject? Unsigned { get; set; }
    public string RawJson { get; set; } = string.Empty;
}
