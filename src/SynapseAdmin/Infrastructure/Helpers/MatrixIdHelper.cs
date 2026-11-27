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

    /// <summary>
    /// Whether <paramref name="id"/> has the form of a full user ID, <c>@localpart:server</c>: an <c>@</c>, a non-empty
    /// localpart, a <c>:</c> and a non-empty server name, without whitespace. The split is at the first <c>:</c>, because
    /// a localpart can't contain one and a server name may end in a port. The homeserver checks the rest (#587).
    /// </summary>
    public static bool IsUserId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id[0] != '@' || id.Any(char.IsWhiteSpace)) return false;
        var colon = id.IndexOf(':');
        return colon > 1 && colon < id.Length - 1;
    }

    /// <summary>
    /// Whether <paramref name="id"/> has the form of a room ID: a <c>!</c>, at least one more character, and no
    /// whitespace. Both forms count: <c>!localpart:domain</c>, and <c>!</c> followed by unpadded URL-safe base64 without
    /// a domain, as room version 12 uses (Synapse's <c>RoomID.from_string</c>). An alias (<c>#…</c>) isn't a room ID
    /// (#605, #606). The homeserver checks the rest.
    /// </summary>
    public static bool IsRoomId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length > 1 && id[0] == '!' && !id.Any(char.IsWhiteSpace);

    /// <summary>
    /// Whether <paramref name="id"/> is a full user ID (<see cref="IsUserId"/>) whose server name equals
    /// <paramref name="serverName"/> exactly. Unlike <see cref="IsOnServer"/>, the comparison is ordinal and
    /// case-sensitive, as Synapse's <c>is_mine</c> and Tuwunel's <c>user_is_local</c> compare it, so that a check that
    /// passes here isn't refused by the homeserver as "not a local user" (#587). False for an empty server name.
    /// </summary>
    public static bool IsLocalUserId(string? id, string? serverName)
    {
        if (string.IsNullOrEmpty(serverName) || id == null || !IsUserId(id)) return false;
        return string.Equals(id[(id.IndexOf(':') + 1)..], serverName, StringComparison.Ordinal);
    }
}
