using System.Net;
using System.Net.Sockets;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using IPNetwork = System.Net.IPNetwork;

namespace SynapseAdmin.Infrastructure.Security;

/// <summary>
/// Default <see cref="IHomeserverDestinationPolicy"/> configured from the <c>HOMESERVER_ALLOWLIST</c> setting.
/// </summary>
/// <remarks>
/// Allowlist entries are separated by commas or semicolons and may be:
/// <list type="bullet">
/// <item><c>host</c> – HTTPS on any port.</item>
/// <item><c>host:port</c> – HTTPS on that port.</item>
/// <item><c>https://host[:port]</c> / <c>http://host[:port]</c> – that scheme (and HTTPS) on that port.</item>
/// <item><c>10.0.0.0/8</c> or a bare IP – exempts those addresses from the internal-address rule.</item>
/// </list>
/// </remarks>
public sealed class HomeserverDestinationPolicy : IHomeserverDestinationPolicy
{
    public const string ConfigurationKey = "HOMESERVER_ALLOWLIST";

    private static readonly IPNetwork[] InternalNetworks =
    [
        IPNetwork.Parse("0.0.0.0/8"),        // "this" network / unspecified
        IPNetwork.Parse("10.0.0.0/8"),       // RFC 1918
        IPNetwork.Parse("100.64.0.0/10"),    // CGNAT (RFC 6598)
        IPNetwork.Parse("127.0.0.0/8"),      // loopback
        IPNetwork.Parse("169.254.0.0/16"),   // link-local (cloud metadata endpoints)
        IPNetwork.Parse("172.16.0.0/12"),    // RFC 1918
        IPNetwork.Parse("192.0.0.0/24"),     // IETF protocol assignments
        IPNetwork.Parse("192.168.0.0/16"),   // RFC 1918
        IPNetwork.Parse("198.18.0.0/15"),    // benchmarking
        IPNetwork.Parse("224.0.0.0/4"),      // multicast
        IPNetwork.Parse("240.0.0.0/4"),      // reserved + broadcast
        IPNetwork.Parse("::/128"),           // unspecified
        IPNetwork.Parse("::1/128"),          // loopback
        IPNetwork.Parse("fc00::/7"),         // unique local (RFC 4193)
        IPNetwork.Parse("fe80::/10"),        // link-local
        IPNetwork.Parse("fec0::/10"),        // deprecated site-local
        IPNetwork.Parse("ff00::/8"),         // multicast
    ];

    private readonly IReadOnlyList<Entry> _entries;

    public HomeserverDestinationPolicy(IEnumerable<string>? allowlistEntries, Action<string>? onInvalidEntry = null)
    {
        var entries = new List<Entry>();
        foreach (var raw in allowlistEntries ?? [])
        {
            if (TryParseEntry(raw, out var entry))
            {
                entries.Add(entry);
            }
            else
            {
                onInvalidEntry?.Invoke(raw);
            }
        }

        _entries = entries;
    }

    /// <summary>Creates the policy from the raw configuration value (comma/semicolon separated).</summary>
    public static HomeserverDestinationPolicy FromConfiguration(string? value, Action<string>? onInvalidEntry = null) =>
        new(string.IsNullOrWhiteSpace(value)
                ? []
                : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            onInvalidEntry);

    public bool HasAllowlist => _entries.Count > 0;

    public void EnsureEntryAllowed(Uri uri)
    {
        EnsureWellFormed(uri);
        var host = NormalizeHost(uri.Host);

        if (HasAllowlist && !_entries.Any(e => e.MatchesHost(host, uri.Port)))
        {
            throw new HomeserverNotAllowedException($"Homeserver '{host}:{uri.Port}' is not in {ConfigurationKey}.");
        }

        EnsureHopAllowed(uri);
    }

    public void EnsureHopAllowed(Uri uri)
    {
        EnsureWellFormed(uri);
        var host = NormalizeHost(uri.Host);

        if (uri.Scheme == Uri.UriSchemeHttp && !_entries.Any(e => e.AllowHttp && e.MatchesHost(host, uri.Port)))
        {
            throw new HomeserverNotAllowedException(
                $"Cleartext HTTP to '{host}:{uri.Port}' is refused; only allowlist entries written as http://host:port may use HTTP.");
        }

        if (IPAddress.TryParse(host, out var literal) && !IsAddressAllowed(host, uri.Port, literal))
        {
            throw new HomeserverNotAllowedException($"Internal address '{literal}' is not allowlisted.");
        }
    }

    public bool IsAddressAllowed(string host, int port, IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (!IsInternal(address))
        {
            return true;
        }

        var normalizedHost = NormalizeHost(host);
        return _entries.Any(e => e.MatchesHost(normalizedHost, port) || e.ContainsAddress(address));
    }

    public string Describe()
    {
        if (!HasAllowlist)
        {
            return "allowlist: (empty - any public HTTPS homeserver), cleartext HTTP: none, internal addresses: refused";
        }

        var http = _entries.Where(e => e.AllowHttp).Select(e => e.ToString()).ToList();
        return $"allowlist: {string.Join(", ", _entries)}, cleartext HTTP: {(http.Count == 0 ? "none" : string.Join(", ", http))}, " +
               "internal addresses: refused unless listed";
    }

    internal static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return InternalNetworks.Any(n => n.BaseAddress.AddressFamily == address.AddressFamily && n.Contains(address));
    }

    private static void EnsureWellFormed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new HomeserverNotAllowedException("Only absolute http(s) homeserver URLs are allowed.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new HomeserverNotAllowedException("Homeserver URLs must not contain user info.");
        }
    }

    private static string NormalizeHost(string host) => host.Trim('[', ']').TrimEnd('.').ToLowerInvariant();

    private static bool TryParseEntry(string raw, out Entry entry)
    {
        entry = default!;
        var value = raw.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host) ||
                uri.AbsolutePath.Trim('/').Length > 0)
            {
                return false;
            }

            entry = new Entry(NormalizeHost(uri.Host), uri.Port, uri.Scheme == Uri.UriSchemeHttp, null, value);
            return true;
        }

        if (value.Contains('/'))
        {
            if (!IPNetwork.TryParse(value, out var network))
            {
                return false;
            }

            entry = new Entry(null, null, false, network, value);
            return true;
        }

        if (IPAddress.TryParse(value, out var address) && (address.AddressFamily == AddressFamily.InterNetworkV6 || !value.Contains(':')))
        {
            entry = new Entry(null, null, false, new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128), value);
            return true;
        }

        if (Uri.TryCreate("https://" + value, UriKind.Absolute, out var hostUri) &&
            !string.IsNullOrEmpty(hostUri.Host) &&
            hostUri.AbsolutePath == "/" &&
            string.IsNullOrEmpty(hostUri.Query))
        {
            entry = new Entry(NormalizeHost(hostUri.Host), hostUri.IsDefaultPort ? null : hostUri.Port, false, null, value);
            return true;
        }

        return false;
    }

    private sealed record Entry(string? Host, int? Port, bool AllowHttp, IPNetwork? Network, string Original)
    {
        public bool MatchesHost(string host, int port) =>
            (Host is not null && string.Equals(Host, host, StringComparison.Ordinal) && (Port is null || Port == port)) ||
            (Network is not null && IPAddress.TryParse(host, out var literal) && ContainsAddress(literal));

        public bool ContainsAddress(IPAddress address)
        {
            if (Network is not { } network)
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            return network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address);
        }

        public override string ToString() => Original;
    }
}
