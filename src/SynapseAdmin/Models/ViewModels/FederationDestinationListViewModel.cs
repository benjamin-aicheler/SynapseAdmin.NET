namespace SynapseAdmin.Models.ViewModels;

public class FederationDestinationListViewModel
{
    public string Destination { get; set; } = string.Empty;
    public long RetryLastTs { get; set; }
    public DateTime? RetryLastTsDateTime => RetryLastTs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(RetryLastTs).DateTime : null;
    public long RetryInterval { get; set; }
    public TimeSpan RetryIntervalTimeSpan => TimeSpan.FromMilliseconds(RetryInterval);
    public long? FailureTs { get; set; }
    public DateTime? FailureTsDateTime => FailureTs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(FailureTs.Value).DateTime : null;
    public string? FailureReason { get; set; }
    public long? LastSuccessfulStreamOrdering { get; set; }
}
