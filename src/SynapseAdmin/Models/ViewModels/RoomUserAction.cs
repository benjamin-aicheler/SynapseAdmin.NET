namespace SynapseAdmin.Models.ViewModels;

/// <summary>The room recovery action that <c>RoomUserActionDialog</c> asks a user ID for (#587).</summary>
public enum RoomUserAction
{
    /// <summary>Grant a user, by default the signed-in admin, the highest power level of a joined local user.</summary>
    MakeAdmin,

    /// <summary>Join a local user to the room.</summary>
    Join
}
