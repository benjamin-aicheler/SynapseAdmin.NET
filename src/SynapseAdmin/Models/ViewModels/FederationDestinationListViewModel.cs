namespace SynapseAdmin.Models.ViewModels;

public class FederationDestinationListViewModel
{
    public string Destination { get; set; } = string.Empty;
    public long RetryLastTs { get; set; }
    public DateTimeOffset? RetryLastTsDateTime => RetryLastTs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(RetryLastTs) : null;

    /// <summary>
    /// Whether the last attempt to reach the destination failed. Synapse sets <c>retry_last_ts</c> to <c>0</c> after a
    /// successful attempt and allows <c>reset_connection</c> only while it isn't <c>0</c>, so this, not
    /// <see cref="FailureTs"/>, decides the status and the reset action (#544).
    /// </summary>
    public bool IsFailing => RetryLastTs > 0;

    public long RetryInterval { get; set; }
    public TimeSpan RetryIntervalTimeSpan => TimeSpan.FromMilliseconds(RetryInterval);
    public long? FailureTs { get; set; }
    public DateTimeOffset? FailureTsDateTime => FailureTs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(FailureTs.Value) : null;
    public long? LastSuccessfulStreamOrdering { get; set; }
}
