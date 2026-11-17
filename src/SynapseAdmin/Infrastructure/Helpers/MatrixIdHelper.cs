namespace SynapseAdmin.Infrastructure.Helpers;

public static class MatrixIdHelper
{
    /// <summary>
    /// Whether a Matrix ID such as <c>@alice:example.org</c> is on <paramref name="serverName"/>: it must end in
    /// <c>:</c> + the server name, compared case-insensitively. Returns false for an empty ID or server name.
    /// Room alias validation doesn't use this, because Synapse compares an alias's server name case-sensitively.
    /// </summary>
    public static bool IsOnServer(string? id, string? serverName)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(serverName)) return false;
        return id.EndsWith($":{serverName}", StringComparison.OrdinalIgnoreCase);
    }
}
