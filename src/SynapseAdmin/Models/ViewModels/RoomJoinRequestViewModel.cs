namespace SynapseAdmin.Models.ViewModels;

/// <summary>
/// What a join dialog returns (#606): <see cref="Target"/> is the room ID or alias on the user page and the user ID on
/// the room page, trimmed; <see cref="Servers"/> are the servers to join through, as entered and split, possibly empty.
/// </summary>
public class RoomJoinRequestViewModel
{
    public string Target { get; init; } = string.Empty;

    public IReadOnlyList<string> Servers { get; init; } = [];
}
