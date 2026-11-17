using MudBlazor;

namespace SynapseAdmin.Extensions;

public static class SortDirectionExtensions
{
    /// <summary>
    /// Maps a table sort direction to the <c>dir</c> parameter of the Synapse Admin list APIs: <c>"f"</c> (forwards) for
    /// <see cref="SortDirection.Ascending"/>, <c>"b"</c> (backwards) for <see cref="SortDirection.Descending"/>, and
    /// <paramref name="whenUnsorted"/> otherwise. Each list passes its own natural order: <c>"f"</c> for rooms and users
    /// (A→Z), <c>"b"</c> for event reports and federation destinations (newest first).
    /// </summary>
    public static string ToSynapseDir(this SortDirection direction, string whenUnsorted) => direction switch
    {
        SortDirection.Ascending => "f",
        SortDirection.Descending => "b",
        _ => whenUnsorted,
    };
}
