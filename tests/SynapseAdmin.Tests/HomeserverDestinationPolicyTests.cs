using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Infrastructure.Gateways;
using SynapseAdmin.Infrastructure.Security;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using Xunit;

namespace SynapseAdmin.Tests;

public class HomeserverDestinationPolicyTests
{
    // ---- Policy rules -------------------------------------------------------------------------

    [Theory]
    [InlineData("https://matrix.org")]
    [InlineData("https://matrix.example.com:8448")]
    [InlineData("https://8.8.8.8")]
    public void EmptyAllowlist_AllowsPublicHttps(string url)
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration(null);

        policy.EnsureEntryAllowed(new Uri(url));
    }

    [Theory]
    [InlineData("http://matrix.org")]                 // cleartext without allowlist entry
    [InlineData("https://127.0.0.1")]                 // loopback
    [InlineData("https://[::1]")]                     // IPv6 loopback
    [InlineData("https://169.254.169.254")]           // link-local / cloud metadata
    [InlineData("https://10.1.2.3")]                  // RFC 1918
    [InlineData("https://172.20.0.5")]                // RFC 1918 (Docker bridge)
    [InlineData("https://192.168.1.10")]              // RFC 1918
    [InlineData("https://100.64.0.1")]                // CGNAT
    [InlineData("https://[fd00::1]")]                 // ULA
    [InlineData("https://[fe80::1]")]                 // IPv6 link-local
    [InlineData("https://[::ffff:127.0.0.1]")]        // IPv4-mapped loopback
    [InlineData("https://0.0.0.0")]                   // unspecified
    [InlineData("https://user:pw@matrix.org")]        // user info
    public void EmptyAllowlist_RefusesCleartextAndInternalTargets(string url)
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration(null);

        Assert.Throws<HomeserverNotAllowedException>(() => policy.EnsureEntryAllowed(new Uri(url)));
    }

    [Fact]
    public void Allowlist_IsHardGateForEntryHost_ButNotForDelegatedHops()
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration("matrix.org");

        policy.EnsureEntryAllowed(new Uri("https://matrix.org"));
        Assert.Throws<HomeserverNotAllowedException>(() => policy.EnsureEntryAllowed(new Uri("https://example.com")));

        // A delegated public host (matrix.org -> matrix-client.matrix.org) does not need its own entry.
        policy.EnsureHopAllowed(new Uri("https://matrix-client.matrix.org"));
    }

    [Fact]
    public void HttpAllowlistEntry_PermitsCleartextAndInternalAddressForThatHostAndPortOnly()
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration("http://synapse:8008");

        policy.EnsureEntryAllowed(new Uri("http://synapse:8008"));
        policy.EnsureEntryAllowed(new Uri("https://synapse:8008"));
        Assert.Throws<HomeserverNotAllowedException>(() => policy.EnsureEntryAllowed(new Uri("http://synapse:8009")));
        Assert.Throws<HomeserverNotAllowedException>(() => policy.EnsureEntryAllowed(new Uri("http://other:8008")));

        var dockerAddress = IPAddress.Parse("172.18.0.2");
        Assert.True(policy.IsAddressAllowed("synapse", 8008, dockerAddress));
        Assert.False(policy.IsAddressAllowed("synapse", 8009, dockerAddress));
        Assert.False(policy.IsAddressAllowed("other", 8008, dockerAddress));
        Assert.True(policy.IsAddressAllowed("other", 443, IPAddress.Parse("93.184.215.14")));
    }

    [Fact]
    public void HostAllowlistEntry_DoesNotPermitCleartext()
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration("synapse.lan:8448");

        policy.EnsureEntryAllowed(new Uri("https://synapse.lan:8448"));
        Assert.Throws<HomeserverNotAllowedException>(() => policy.EnsureEntryAllowed(new Uri("http://synapse.lan:8448")));
    }

    [Fact]
    public void CidrAllowlistEntry_ExemptsAddressesFromInternalRule()
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration("10.0.0.0/8; matrix.lan");

        Assert.True(policy.IsAddressAllowed("any.host", 443, IPAddress.Parse("10.4.5.6")));
        Assert.False(policy.IsAddressAllowed("any.host", 443, IPAddress.Parse("192.168.0.1")));
        policy.EnsureEntryAllowed(new Uri("https://10.4.5.6"));
    }

    [Fact]
    public void AllowlistWithOnlyInvalidEntries_FailsClosed()
    {
        var invalid = new List<string>();
        var policy = HomeserverDestinationPolicy.FromConfiguration("https://matrix.example.com/invalid-path", invalid.Add);

        Assert.Single(invalid);
        Assert.True(policy.HasAllowlist);
        Assert.Throws<HomeserverNotAllowedException>(() => policy.EnsureEntryAllowed(new Uri("https://matrix.org")));
        Assert.Contains("ALL homeservers are refused", policy.Describe());
    }

    [Fact]
    public void ExplicitDefaultPortInHostEntry_IsNotTreatedAsWildcard()
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration("synapse.lan:443");

        policy.EnsureEntryAllowed(new Uri("https://synapse.lan"));
        Assert.Throws<HomeserverNotAllowedException>(() => policy.EnsureEntryAllowed(new Uri("https://synapse.lan:8448")));
        Assert.False(policy.IsAddressAllowed("synapse.lan", 8448, IPAddress.Parse("10.0.0.5")));
        Assert.True(policy.IsAddressAllowed("synapse.lan", 443, IPAddress.Parse("10.0.0.5")));
    }

    [Fact]
    public void HostEntryWithoutPort_MatchesAnyPort()
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration("synapse.lan");

        policy.EnsureEntryAllowed(new Uri("https://synapse.lan:8448"));
    }

    [Fact]
    public void InvalidAllowlistEntries_AreReportedAndIgnored()
    {
        var invalid = new List<string>();
        var policy = HomeserverDestinationPolicy.FromConfiguration("ftp://x, 10.0.0.0/99, https://ok.example/path, good.example", invalid.Add);

        Assert.Equal(["ftp://x", "10.0.0.0/99", "https://ok.example/path"], invalid);
        Assert.True(policy.HasAllowlist);
        Assert.Contains("good.example", policy.Describe());
    }

    // ---- End-to-end against controlled loopback endpoints ----------------------------------------

    [Fact]
    public async Task ApprovedInternalHomeserver_LogsInOverAllowlistedHttp()
    {
        using var homeserver = new LoopbackServer(req => req.Path switch
        {
            "/_matrix/client/versions" => (200, """{"versions":["v1.11"]}""", null),
            "/_matrix/client/v3/login" => (200, """{"access_token":"syt_ok","user_id":"@alice:internal","device_id":"DEV"}""", null),
            _ => (404, """{"errcode":"M_UNRECOGNIZED"}""", null)
        });
        var gateway = CreateGateway($"http://127.0.0.1:{homeserver.Port}");

        var login = await gateway.LoginAsync(homeserver.BaseUrl, "alice", "S3cret");

        Assert.Equal("syt_ok", login.AccessToken);
        var loginRequest = Assert.Single(homeserver.Requests, r => r.Path == "/_matrix/client/v3/login");
        Assert.Equal("POST", loginRequest.Method);
        Assert.Contains("\"password\":\"S3cret\"", loginRequest.Body);
    }

    [Fact]
    public async Task DirectInternalHomeserver_IsRefusedBeforeAnyRequest()
    {
        using var homeserver = new LoopbackServer(_ => (200, "{}", null));
        var gateway = CreateGateway(allowlist: null);

        await Assert.ThrowsAsync<HomeserverNotAllowedException>(() => gateway.LoginAsync(homeserver.BaseUrl, "alice", "S3cret"));

        Assert.Empty(homeserver.Requests);
    }

    [Fact]
    public async Task UnlistedHomeserver_IsRefusedByAllowlistBeforeAnyRequest()
    {
        using var homeserver = new LoopbackServer(_ => (200, "{}", null));
        var gateway = CreateGateway("https://matrix.example.com");

        await Assert.ThrowsAsync<HomeserverNotAllowedException>(() => gateway.ResolveHomeserverAsync(homeserver.BaseUrl));

        Assert.Empty(homeserver.Requests);
    }

    [Fact]
    public async Task DelegationToUnlistedInternalTarget_IsRefusedAndCredentialsAreNeverSent()
    {
        using var delegated = new LoopbackServer(_ => (200, """{"access_token":"x","user_id":"@x:y"}""", null));
        using var entry = new LoopbackServer(req => req.Path == "/.well-known/matrix/client"
            ? (200, $$$"""{"m.homeserver":{"base_url":"http://127.0.0.1:{{{delegated.Port}}}"}}""", null)
            : (404, "{}", null));
        var gateway = CreateGateway($"http://127.0.0.1:{entry.Port}");

        await Assert.ThrowsAsync<HomeserverNotAllowedException>(() => gateway.LoginAsync(entry.BaseUrl, "alice", "S3cret"));

        Assert.Empty(delegated.Requests);
    }

    [Fact]
    public async Task DelegationToHostnameResolvingInternally_IsRefusedAtConnectTime()
    {
        // "localhost" passes the string check (it is not an IP literal) but resolves to loopback;
        // the connect-time check must refuse it before any TCP connection is opened.
        using var delegated = new LoopbackServer(_ => (200, "{}", null));
        using var entry = new LoopbackServer(req => req.Path == "/.well-known/matrix/client"
            ? (200, $$$"""{"m.homeserver":{"base_url":"https://localhost:{{{delegated.Port}}}"}}""", null)
            : (404, "{}", null));
        var gateway = CreateGateway($"http://127.0.0.1:{entry.Port}");

        var ex = await Assert.ThrowsAsync<HomeserverNotAllowedException>(() => gateway.LoginAsync(entry.BaseUrl, "alice", "S3cret"));

        Assert.Contains("resolved to internal address", ex.Message);
        Assert.Empty(delegated.Requests);
    }

    [Fact]
    public async Task RedirectToUnlistedInternalTarget_IsRefused()
    {
        using var target = new LoopbackServer(_ => (200, """{"m.homeserver":{"base_url":"https://matrix.org"}}""", null));
        using var entry = new LoopbackServer(req => req.Path == "/.well-known/matrix/client"
            ? (302, "", $"http://127.0.0.1:{target.Port}/.well-known/matrix/client")
            : (404, "{}", null));
        var gateway = CreateGateway($"http://127.0.0.1:{entry.Port}");

        await Assert.ThrowsAsync<HomeserverNotAllowedException>(() => gateway.ResolveHomeserverAsync(entry.BaseUrl));

        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task LoginRedirect_IsNotFollowed()
    {
        using var target = new LoopbackServer(_ => (200, """{"access_token":"x","user_id":"@x:y"}""", null));
        using var homeserver = new LoopbackServer(req => req.Path switch
        {
            "/_matrix/client/versions" => (200, """{"versions":["v1.11"]}""", null),
            "/_matrix/client/v3/login" => (307, "", $"http://127.0.0.1:{target.Port}/_matrix/client/v3/login"),
            _ => (404, "{}", null)
        });
        var gateway = CreateGateway($"http://127.0.0.1:{homeserver.Port},http://127.0.0.1:{target.Port}");

        await Assert.ThrowsAnyAsync<Exception>(() => gateway.LoginAsync(homeserver.BaseUrl, "alice", "S3cret"));

        Assert.Empty(target.Requests);
    }

    [Fact]
    public async Task UpstreamRateLimit_IsSurfacedAsMatrixError()
    {
        using var homeserver = new LoopbackServer(req => req.Path switch
        {
            "/_matrix/client/versions" => (200, """{"versions":["v1.11"]}""", null),
            "/_matrix/client/v3/login" => (429, """{"errcode":"M_LIMIT_EXCEEDED","error":"Too many requests"}""", null),
            _ => (404, "{}", null)
        });
        var gateway = CreateGateway($"http://127.0.0.1:{homeserver.Port}");

        var ex = await Assert.ThrowsAsync<LibMatrix.LibMatrixException>(() => gateway.LoginAsync(homeserver.BaseUrl, "alice", "S3cret"));

        Assert.Equal("M_LIMIT_EXCEEDED", ex.ErrorCode);
    }

    // ---- Discovery URL normalization and failure logging ----------------------------------------

    /// <summary>
    /// The entered, the delegated client and the delegated server URL are each reduced to scheme, authority and path,
    /// without trailing slashes. Query and fragment are dropped, and follow-up requests are built from the result.
    /// </summary>
    [Fact]
    public async Task Discover_NormalizesEnteredAndDelegatedUrls()
    {
        using var target = new LoopbackServer(req => req.Path == "/fed/_matrix/federation/v1/version"
            ? (200, """{"server":{"name":"Synapse","version":"1.120.0"}}""", null)
            : (404, "{}", null));
        using var entry = new LoopbackServer(req => req.Path switch
        {
            "/.well-known/matrix/client" => (200, $$$"""{"m.homeserver":{"base_url":"{{{target.BaseUrl}}}/client/?via=wk#top"}}""", null),
            "/.well-known/matrix/server" => (200, $$$"""{"m.server":"{{{target.BaseUrl}}}/fed/?via=wk"}""", null),
            _ => (404, "{}", null)
        });
        using var discovery = CreateDiscoveryClient($"http://127.0.0.1:{entry.Port},http://127.0.0.1:{target.Port}");

        var endpoints = await discovery.DiscoverAsync($"{entry.BaseUrl}/?via=entry");

        Assert.Equal(new HomeserverEndpoints($"{target.BaseUrl}/client", $"{target.BaseUrl}/fed", "Synapse", "1.120.0"), endpoints);
        Assert.Equal(["/.well-known/matrix/client", "/.well-known/matrix/server"], entry.Requests.Select(r => r.Path));
        Assert.Equal(["/fed/_matrix/federation/v1/version"], target.Requests.Select(r => r.Path));
    }

    /// <summary>
    /// A failed discovery request (unreadable JSON, too many redirects) is logged at Debug level with the exception and
    /// the sanitized URL. The long entry path shows the sanitizer at work: it truncates after 1024 characters.
    /// </summary>
    [Fact]
    public async Task Discover_LogsFailedRequestsAtDebugWithSanitizedUrl()
    {
        using var entry = new LoopbackServer(req =>
            req.Path.EndsWith("/.well-known/matrix/client", StringComparison.Ordinal) ? (200, "not json", null)
            : req.Path.EndsWith("/_matrix/client/versions", StringComparison.Ordinal) ? (302, "", req.Path)
            : (404, "{}", null));
        var logger = new RecordingLogger();
        using var discovery = CreateDiscoveryClient($"http://127.0.0.1:{entry.Port}", logger);
        var entryUrl = $"{entry.BaseUrl}/{new string('a', 1100)}";

        await Assert.ThrowsAsync<InvalidOperationException>(() => discovery.DiscoverAsync(entryUrl));

        Assert.Collection(logger.Entries,
            e => AssertDiscoveryFailureLogged<JsonException>(e, $"{entryUrl}/.well-known/matrix/client"),
            e => AssertDiscoveryFailureLogged<HttpRequestException>(e, $"{entryUrl}/_matrix/client/versions"));
    }

    private static void AssertDiscoveryFailureLogged<TException>(LogEntry entry, string url) where TException : Exception
    {
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal("Discovery request to {Url} failed", entry.Properties["{OriginalFormat}"]);
        Assert.Equal(url[..1024] + "...(truncated)", entry.Properties["Url"]);
        Assert.IsType<TException>(entry.Exception);
    }

    private static MatrixAuthGateway CreateGateway(string? allowlist)
    {
        var policy = HomeserverDestinationPolicy.FromConfiguration(allowlist);
        var discovery = new HomeserverDiscoveryClient(policy, NullLogger<HomeserverDiscoveryClient>.Instance);
        return new MatrixAuthGateway(discovery, new MemoryCache(new MemoryCacheOptions()));
    }

    private static HomeserverDiscoveryClient CreateDiscoveryClient(string allowlist, ILogger<HomeserverDiscoveryClient>? logger = null) =>
        new(HomeserverDestinationPolicy.FromConfiguration(allowlist), logger ?? NullLogger<HomeserverDiscoveryClient>.Instance);

    private sealed record LogEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Properties, Exception? Exception);

    /// <summary>Records every entry with its structured properties; all levels are enabled.</summary>
    private sealed class RecordingLogger : ILogger<HomeserverDiscoveryClient>
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            _entries.Enqueue(new LogEntry(logLevel, properties.ToDictionary(p => p.Key, p => p.Value), exception));
        }
    }

    private sealed record RecordedRequest(string Method, string Path, string Body);

    /// <summary>Minimal HTTP/1.1 server on 127.0.0.1 that records every request it receives.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Func<RecordedRequest, (int Status, string Body, string? Location)> _handler;
        private readonly ConcurrentQueue<RecordedRequest> _requests = new();

        public LoopbackServer(Func<RecordedRequest, (int Status, string Body, string? Location)> handler)
        {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = AcceptLoopAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public string BaseUrl => $"http://127.0.0.1:{Port}";
        public IReadOnlyCollection<RecordedRequest> Requests => _requests.ToArray();

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch
                {
                    return;
                }

                _ = HandleAsync(client);
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                    var requestLine = await reader.ReadLineAsync(_cts.Token);
                    if (string.IsNullOrEmpty(requestLine))
                    {
                        return;
                    }

                    var parts = requestLine.Split(' ');
                    var contentLength = 0;
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_cts.Token)))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            contentLength = int.Parse(line["Content-Length:".Length..].Trim());
                        }
                    }

                    var buffer = new char[contentLength];
                    var read = 0;
                    while (read < contentLength)
                    {
                        var n = await reader.ReadAsync(buffer.AsMemory(read), _cts.Token);
                        if (n == 0) break;
                        read += n;
                    }

                    var request = new RecordedRequest(parts[0], parts[1].Split('?')[0], new string(buffer, 0, read));
                    _requests.Enqueue(request);

                    var (status, body, location) = _handler(request);
                    var bodyBytes = Encoding.UTF8.GetBytes(body);
                    var header = new StringBuilder()
                        .Append($"HTTP/1.1 {status} X\r\n")
                        .Append("Content-Type: application/json\r\n")
                        .Append($"Content-Length: {bodyBytes.Length}\r\n")
                        .Append("Connection: close\r\n");
                    if (location is not null)
                    {
                        header.Append($"Location: {location}\r\n");
                    }

                    header.Append("\r\n");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()), _cts.Token);
                    await stream.WriteAsync(bodyBytes, _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
                catch
                {
                    // Test server: ignore broken connections.
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
