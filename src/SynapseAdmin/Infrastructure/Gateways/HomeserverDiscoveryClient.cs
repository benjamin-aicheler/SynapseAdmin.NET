using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibMatrix;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Implements homeserver discovery and password login with a dedicated <see cref="HttpClient"/> whose
/// connections are validated against <see cref="IHomeserverDestinationPolicy"/>:
/// <list type="bullet">
/// <item>Every URL (entered, delegated, redirected) is checked before a request is sent.</item>
/// <item>A <see cref="SocketsHttpHandler.ConnectCallback"/> resolves the host once, validates every returned
/// address and connects to a validated address, so DNS rebinding cannot reach an internal target.</item>
/// <item>Redirects are followed manually (GET only) and re-validated; the login POST never follows redirects.</item>
/// <item>No outbound proxy is used, because the connect-time check must see the real destination.</item>
/// </list>
/// LibMatrix's resolver is deliberately not used here: it follows delegation and redirects without any
/// destination checks and its HTTP client cannot be configured from the outside.
/// </summary>
public sealed class HomeserverDiscoveryClient : IHomeserverDiscoveryClient, IDisposable
{
    private const int MaxRedirects = 3;
    private const string DefaultLoginDeviceName = "SynapseAdmin.NET";

    private readonly IHomeserverDestinationPolicy _policy;
    private readonly ILogger<HomeserverDiscoveryClient> _logger;
    private readonly HttpClient _httpClient;

    public HomeserverDiscoveryClient(IHomeserverDestinationPolicy policy, ILogger<HomeserverDiscoveryClient> logger)
    {
        _policy = policy;
        _logger = logger;

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = ConnectAsync
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 1024 * 1024
        };
    }

    public async Task<HomeserverEndpoints> DiscoverAsync(string homeserverUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(homeserverUrl.Trim(), UriKind.Absolute, out var entry))
        {
            throw new ArgumentException("Invalid homeserver URL. Must be an absolute HTTP or HTTPS URI.", nameof(homeserverUrl));
        }

        _policy.EnsureEntryAllowed(entry);
        var entryBase = entry.GetLeftPart(UriPartial.Path).TrimEnd('/');

        // Client-server API: .well-known/matrix/client delegation, otherwise the entered URL itself.
        string? clientUrl = null;
        var clientWellKnown = await TryGetJsonAsync<ClientWellKnownDto>($"{entryBase}/.well-known/matrix/client", cancellationToken);
        if (!string.IsNullOrWhiteSpace(clientWellKnown?.Homeserver?.BaseUrl))
        {
            clientUrl = clientWellKnown.Homeserver.BaseUrl.Trim().TrimEnd('/');
        }
        else if (await IsSuccessAsync($"{entryBase}/_matrix/client/versions", cancellationToken))
        {
            clientUrl = entryBase;
        }

        if (clientUrl is null)
        {
            throw new InvalidOperationException("Could not resolve a client URL for the homeserver.");
        }

        var clientUri = ParseHopUri(clientUrl);
        _policy.EnsureHopAllowed(clientUri);
        clientUrl = clientUri.GetLeftPart(UriPartial.Path).TrimEnd('/');

        // Federation: .well-known/matrix/server delegation, otherwise the client URL.
        var serverCandidate = clientUrl;
        var serverWellKnown = await TryGetJsonAsync<ServerWellKnownDto>($"{entryBase}/.well-known/matrix/server", cancellationToken);
        if (!string.IsNullOrWhiteSpace(serverWellKnown?.Server))
        {
            var delegated = serverWellKnown.Server.Trim().TrimEnd('/');
            serverCandidate = delegated.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                              delegated.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? delegated
                : $"https://{delegated}";
        }

        var serverUri = ParseHopUri(serverCandidate);
        _policy.EnsureHopAllowed(serverUri);
        serverCandidate = serverUri.GetLeftPart(UriPartial.Path).TrimEnd('/');

        var federationVersion = await TryGetJsonAsync<FederationVersionDto>($"{serverCandidate}/_matrix/federation/v1/version", cancellationToken);
        var serverUrl = federationVersion is not null ? serverCandidate : null;

        return new HomeserverEndpoints(clientUrl, serverUrl, federationVersion?.Server?.Name, federationVersion?.Server?.Version);
    }

    public async Task<LoginResponse> LoginAsync(string clientBaseUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var loginUri = ParseHopUri($"{clientBaseUrl.TrimEnd('/')}/_matrix/client/v3/login");
        _policy.EnsureHopAllowed(loginUri);

        var payload = new LoginRequestDto
        {
            Identifier = new LoginIdentifierDto { User = username },
            Password = password,
            InitialDeviceDisplayName = DefaultLoginDeviceName
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, loginUri)
        {
            // Buffered (Content-Length) rather than chunked, for compatibility with proxies in front of homeservers.
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        // Redirects are never followed for credential-bearing requests.
        using var response = await SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateErrorAsync(response, cancellationToken);
        }

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
        if (login is null || string.IsNullOrEmpty(login.AccessToken) || string.IsNullOrEmpty(login.UserId))
        {
            throw new InvalidOperationException("Homeserver returned an invalid login response.");
        }

        return login;
    }

    public void Dispose() => _httpClient.Dispose();

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        // All addresses must pass: a mixed public/internal answer is treated as an attack.
        foreach (var address in addresses)
        {
            if (!_policy.IsAddressAllowed(host, port, address))
            {
                throw new HomeserverNotAllowedException($"Host '{host}' resolved to internal address '{address}', which is not allowlisted.");
            }
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                socket.Dispose();
                lastError = ex;
            }
        }

        throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (FindPolicyRejection(ex) is { } rejection)
        {
            throw rejection;
        }
    }

    /// <summary>GET with manual, policy-checked redirect handling.</summary>
    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken)
    {
        var uri = ParseHopUri(url);
        for (var hop = 0; ; hop++)
        {
            _policy.EnsureHopAllowed(uri);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await SendAsync(request, cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                if (hop >= MaxRedirects)
                {
                    throw new HttpRequestException("Too many redirects during homeserver discovery.");
                }

                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }

            return response;
        }
    }

    private async Task<T?> TryGetJsonAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        try
        {
            using var response = await GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or SocketException or IOException or UriFormatException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogDebug(ex, "Discovery request to {Url} failed", url.SanitizeForLogging());
            return null;
        }
    }

    private async Task<bool> IsSuccessAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or UriFormatException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogDebug(ex, "Discovery request to {Url} failed", url.SanitizeForLogging());
            return false;
        }
    }

    private static Uri ParseHopUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new HomeserverNotAllowedException("Delegated homeserver URL is not an absolute URI.");
        }

        return uri;
    }

    private static HomeserverNotAllowedException? FindPolicyRejection(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is HomeserverNotAllowedException rejection)
            {
                return rejection;
            }
        }

        return null;
    }

    private static async Task<Exception> CreateErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        MatrixErrorDto? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<MatrixErrorDto>(cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            // Not a Matrix error body.
        }

        if (!string.IsNullOrEmpty(error?.ErrorCode))
        {
            return new LibMatrixException { ErrorCode = error.ErrorCode, Error = error.Error ?? string.Empty };
        }

        return new HttpRequestException($"Login request failed with status {(int)response.StatusCode}.", null, response.StatusCode);
    }

    private sealed class ClientWellKnownDto
    {
        [JsonPropertyName("m.homeserver")]
        public WellKnownHomeserverDto? Homeserver { get; set; }
    }

    private sealed class WellKnownHomeserverDto
    {
        [JsonPropertyName("base_url")]
        public string? BaseUrl { get; set; }
    }

    private sealed class ServerWellKnownDto
    {
        [JsonPropertyName("m.server")]
        public string? Server { get; set; }
    }

    private sealed class FederationVersionDto
    {
        [JsonPropertyName("server")]
        public FederationServerDto? Server { get; set; }
    }

    private sealed class FederationServerDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    private sealed class MatrixErrorDto
    {
        [JsonPropertyName("errcode")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class LoginRequestDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "m.login.password";

        [JsonPropertyName("identifier")]
        public LoginIdentifierDto Identifier { get; set; } = new();

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("initial_device_display_name")]
        public string InitialDeviceDisplayName { get; set; } = "";
    }

    private sealed class LoginIdentifierDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "m.id.user";

        [JsonPropertyName("user")]
        public string User { get; set; } = "";
    }
}
