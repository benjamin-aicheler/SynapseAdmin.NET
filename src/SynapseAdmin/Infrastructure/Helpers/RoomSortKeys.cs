using System.Collections.Frozen;

namespace SynapseAdmin.Infrastructure.Helpers;

/// <summary>
/// Sort order of the <c>order_by</c> keys of <c>GET /_synapse/admin/v1/rooms</c> (#541). Synapse documents these keys
/// as largest to smallest with <c>dir=f</c>; every other key sorts ascending. <c>dir=b</c> reverses either order.
/// Tuwunel sorts every key ascending, so <c>SynapseCompatibleAdminGateway</c> reverses the <c>dir</c> for these keys.
/// </summary>
public static class RoomSortKeys
{
    private static readonly FrozenSet<string> LargestFirstKeys = new[]
    {
        "joined_members", "joined_local_members", "version", "state_events", "size",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Whether Synapse sorts <paramref name="orderBy"/> largest first with <c>dir=f</c>. Synapse's keys are case-sensitive.</summary>
    public static bool IsLargestFirst(string orderBy) => LargestFirstKeys.Contains(orderBy);

    /// <summary>The opposite <c>dir</c>: <c>"b"</c> for <c>"f"</c>, otherwise <c>"f"</c>.</summary>
    public static string Reverse(string dir) => dir == "f" ? "b" : "f";
}
