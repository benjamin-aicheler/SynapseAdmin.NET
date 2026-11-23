namespace SynapseAdmin.Models.ViewModels;

/// <summary>
/// One room deletion job (#518). Synapse's error text for a failed deletion is not part of it: it goes only to the log.
/// </summary>
public class RoomDeletionStatusViewModel
{
    public string DeleteId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public RoomDeletionState State { get; set; }

    /// <summary>Whether the deletion still has to finish: the room page shows its banner and checks it.</summary>
    public bool IsRunning => State is RoomDeletionState.Scheduled or RoomDeletionState.Active;
}
