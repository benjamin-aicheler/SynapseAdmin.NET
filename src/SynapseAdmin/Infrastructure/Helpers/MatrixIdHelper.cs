using System.Text.RegularExpressions;

namespace SynapseAdmin.Infrastructure.Helpers;

public static class MatrixIdHelper
{
    /// <summary>
    /// The Matrix spec's server name: a DNS name, an IPv4 address or a bracketed IPv6 address, with an optional port.
    /// <c>\z</c>, not <c>$</c>: <c>$</c> also matches before a final line break.
    /// </summary>
    private static readonly Regex ServerNamePattern = new(
        @"^(\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9.-]+)(:[0-9]{1,5})?\z", RegexOptions.CultureInvariant);

    private const int MaxServerNameLength = 255;

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
    /// Whether <paramref name="id"/> has the form of a room alias, <c>#localpart:server</c> (#606): a <c>#</c>, a localpart
    /// that isn't empty or blank, a <c>:</c> and a server name (<see cref="IsServerName"/>). The split is at the first
    /// <c>:</c>, as Synapse splits it. The localpart may contain spaces (#580), but no control characters, and the alias
    /// has no whitespace at its ends. The homeserver checks the rest.
    /// </summary>
    public static bool IsRoomAlias(string? id)
    {
        if (string.IsNullOrEmpty(id) || id[0] != '#' || id != id.Trim() || id.Any(char.IsControl)) return false;
        var colon = id.IndexOf(':');
        return colon > 1 && !string.IsNullOrWhiteSpace(id[1..colon]) && IsServerName(id[(colon + 1)..]);
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a server name as the Matrix spec defines it: a DNS name, an IPv4 address or a
    /// bracketed IPv6 address, with an optional port, at most 255 characters. The join dialogs take the servers to join
    /// through in this form (#606).
    /// </summary>
    public static bool IsServerName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= MaxServerNameLength && ServerNamePattern.IsMatch(name);

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
