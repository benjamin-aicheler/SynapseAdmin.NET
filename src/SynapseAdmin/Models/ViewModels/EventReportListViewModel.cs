namespace SynapseAdmin.Models.ViewModels;

public class EventReportListViewModel
{
    public string Id { get; set; } = string.Empty;
    public long ReceivedTs { get; set; }
    public DateTime ReceivedTsDateTime => DateTimeOffset.FromUnixTimeMilliseconds(ReceivedTs).DateTime;
    public string UserId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int? Score { get; set; }
    public string? Sender { get; set; }
    public string? CanonicalAlias { get; set; }
}
