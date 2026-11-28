namespace SynapseAdmin.Models.ViewModels;

/// <summary>An event report with the reported event, for the details dialog (#588).</summary>
public class EventReportDetailViewModel
{
    public string Id { get; set; } = string.Empty;
    public long ReceivedTs { get; set; }
    public DateTimeOffset ReceivedTsDateTime => DateTimeOffset.FromUnixTimeMilliseconds(ReceivedTs);
    public string UserId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string? RoomName { get; set; }
    public string? CanonicalAlias { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int? Score { get; set; }
    public string? Sender { get; set; }

    /// <summary>The reported event as the homeserver stores it, indented; empty when it sent none. Never log it.</summary>
    public string EventJson { get; set; } = string.Empty;
}
