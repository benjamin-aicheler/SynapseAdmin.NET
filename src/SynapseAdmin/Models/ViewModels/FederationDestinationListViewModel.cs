namespace SynapseAdmin.Models.ViewModels;

public class FederationDestinationListViewModel
{
    public string Destination { get; set; } = string.Empty;
    public long RetryLastTs { get; set; }
    public DateTimeOffset? RetryLastTsDateTime => RetryLastTs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(RetryLastTs) : null;
    public long RetryInterval { get; set; }
    public TimeSpan RetryIntervalTimeSpan => TimeSpan.FromMilliseconds(RetryInterval);
    public long? FailureTs { get; set; }
    public DateTimeOffset? FailureTsDateTime => FailureTs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(FailureTs.Value) : null;
    public long? LastSuccessfulStreamOrdering { get; set; }
}
