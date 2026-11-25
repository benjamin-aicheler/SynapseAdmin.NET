namespace SynapseAdmin.Models.ViewModels;

/// <summary>
/// Whether a room is blocked, which keeps local users from joining it (#586). <see cref="BlockedBy"/> is the user who
/// blocked it, empty when the homeserver doesn't report one.
/// </summary>
public class RoomBlockStatusViewModel
{
    public bool IsBlocked { get; set; }
    public string BlockedBy { get; set; } = string.Empty;
}
