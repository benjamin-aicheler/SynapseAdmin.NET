using System.Net;

namespace SynapseAdmin.Interfaces;

/// <summary>
/// Decides which homeserver destinations the application may contact during discovery and login.
/// </summary>
/// <remarks>
/// Rules:
/// <list type="bullet">
/// <item>If an allowlist is configured, the homeserver the user enters must match an entry (hard gate).</item>
/// <item>Cleartext <c>http://</c> is only permitted for allowlist entries written as <c>http://host:port</c>.</item>
/// <item>Loopback, link-local, private, CGNAT, ULA, multicast and unspecified addresses are refused
/// unless the host or address is matched by an allowlist entry. This applies to every hop
/// (entered URL, <c>.well-known</c> delegation, redirects) and to every resolved address.</item>
/// </list>
/// </remarks>
public interface IHomeserverDestinationPolicy
{
    /// <summary>True when <c>HOMESERVER_ALLOWLIST</c> contains at least one valid entry.</summary>
    bool HasAllowlist { get; }

    /// <summary>
    /// Validates the homeserver URL entered by the user (allowlist gate, scheme and literal-address rules).
    /// Throws <see cref="Models.HomeserverNotAllowedException"/> when refused.
    /// </summary>
    void EnsureEntryAllowed(Uri uri);

    /// <summary>
    /// Validates a URL reached through delegation or redirect (scheme and literal-address rules, no allowlist gate).
    /// Throws <see cref="Models.HomeserverNotAllowedException"/> when refused.
    /// </summary>
    void EnsureHopAllowed(Uri uri);

    /// <summary>Returns whether a resolved address may be connected to for the given host and port.</summary>
    bool IsAddressAllowed(string host, int port, IPAddress address);

    /// <summary>Human-readable description of the effective policy for startup logging.</summary>
    string Describe();
}
