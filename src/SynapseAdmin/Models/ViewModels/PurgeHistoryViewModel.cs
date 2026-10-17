namespace SynapseAdmin.Models.ViewModels;

public class PurgeHistoryViewModel
{
    public bool? DeleteLocalEvents { get; set; }
    public string? PurgeUpToEventId { get; set; }
    public long? PurgeUpToTs { get; set; }
}
