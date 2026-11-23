namespace SynapseAdmin.Models.ViewModels;

/// <summary>
/// Where a room deletion stands, from Synapse's <c>status</c> (#518). <see cref="Unknown"/> covers a deletion Synapse no
/// longer knows (its status is kept 24 hours after it finishes, or until Synapse restarts) and any status this app
/// doesn't know: the status API is experimental.
/// </summary>
public enum RoomDeletionState
{
    Unknown,
    Scheduled,
    Active,
    Complete,
    Failed
}
