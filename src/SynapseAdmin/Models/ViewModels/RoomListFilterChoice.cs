namespace SynapseAdmin.Models.ViewModels;

/// <summary>
/// One of the room list's filters (#588), for the optional <c>public_rooms</c> and <c>empty_rooms</c> flags of
/// <c>GET /_synapse/admin/v1/rooms</c>. Synapse treats a missing flag as "both", so a filter has three states.
/// </summary>
public enum RoomListFilterChoice
{
    /// <summary>Leave the flag out: list matching and non-matching rooms.</summary>
    All,

    /// <summary>Send <c>true</c>: list only the matching rooms.</summary>
    Only,

    /// <summary>Send <c>false</c>: leave the matching rooms out.</summary>
    Exclude
}

public static class RoomListFilterChoiceExtensions
{
    /// <summary>The flag to send: <c>null</c> for <see cref="RoomListFilterChoice.All"/>.</summary>
    public static bool? ToFlag(this RoomListFilterChoice choice) => choice switch
    {
        RoomListFilterChoice.Only => true,
        RoomListFilterChoice.Exclude => false,
        _ => null
    };
}
