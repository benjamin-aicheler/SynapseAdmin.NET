using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LibMatrix;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using Microsoft.Extensions.Caching.Memory;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Implementation of IMatrixAuthGateway. Centralizes unauthenticated "wire-level" entry points.
/// Discovery and password login run through <see cref="IHomeserverDiscoveryClient"/>, which enforces the
/// homeserver destination policy; LibMatrix only ever receives the already validated base URLs.
/// </summary>
public class MatrixAuthGateway(IHomeserverDiscoveryClient discoveryClient, IMemoryCache cache) : IMatrixAuthGateway
{
    private static readonly ConditionalWeakTable<IMemoryCache, ConcurrentDictionary<string, Lazy<Task<IMatrixGateway>>>> InFlightByCache = new();
    private static readonly TimeSpan DiscoveryCacheDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The oldest Synapse release that serves every admin endpoint and parameter the app sends: 1.145.0 added the
    /// user memberships list (SPEC.md §10, "Synapse versions", #612).
    /// </summary>
    private static readonly Version MinimumSynapseVersion = new(1, 145, 0);

    /// <summary>
    /// The release number at the start of a Synapse version string, which may go on with a pre-release or build part
    /// (<c>1.145.0rc1</c>, <c>1.165.0 (b=develop, …)</c>). Only the start of the string counts.
    /// </summary>
    private static readonly Regex SynapseReleasePattern = new(@"^(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant);

    private static void ValidateHomeserverUrl(string homeserverUrl)
    {
        if (string.IsNullOrWhiteSpace(homeserverUrl) ||
            !Uri.TryCreate(homeserverUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Invalid homeserver URL. Must be an absolute HTTP or HTTPS URI.", nameof(homeserverUrl));
        }
    }

    private static string NormalizeHomeserverUrl(string homeserverUrl) => homeserverUrl.Trim().TrimEnd('/').ToLowerInvariant();

    private static string GetGatewayCacheKey(string homeserverUrl, string accessToken)
    {
        var normalizedHs = NormalizeHomeserverUrl(homeserverUrl);
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
        return $"matrix_gateway:{normalizedHs}:{tokenHash}";
    }

    /// <summary>
    /// Discovers (and briefly caches) the policy-validated endpoints for a homeserver.
    /// Failures and policy rejections are never cached.
    /// </summary>
    private async Task<HomeserverEndpoints> GetEndpointsAsync(string homeserverUrl, CancellationToken cancellationToken)
    {
        ValidateHomeserverUrl(homeserverUrl);

        var cacheKey = $"matrix_discovery:{NormalizeHomeserverUrl(homeserverUrl)}";
        if (cache.TryGetValue(cacheKey, out HomeserverEndpoints? cached) && cached is not null)
        {
            return cached;
        }

        var endpoints = await discoveryClient.DiscoverAsync(homeserverUrl, cancellationToken);
        cache.Set(cacheKey, endpoints, DiscoveryCacheDuration);
        return endpoints;
    }

    public async Task ResolveHomeserverAsync(string homeserverUrl, CancellationToken cancellationToken = default)
    {
        await GetEndpointsAsync(homeserverUrl, cancellationToken);
    }

    public async Task<LoginResponse> LoginAsync(string homeserverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var endpoints = await GetEndpointsAsync(homeserverUrl, cancellationToken);
        return await discoveryClient.LoginAsync(endpoints.ClientUrl, username, password, cancellationToken);
    }

    public async Task<IMatrixGateway> GetAuthenticatedAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken = default)
    {
        ValidateHomeserverUrl(homeserverUrl);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = GetGatewayCacheKey(homeserverUrl, accessToken);
        if (cache.TryGetValue(cacheKey, out IMatrixGateway? cachedGateway) && cachedGateway != null)
        {
            return cachedGateway;
        }

        var inFlightMap = InFlightByCache.GetOrCreateValue(cache);

        var lazyTask = inFlightMap.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<IMatrixGateway>>(
                () => CreateAndCacheGatewayAsync(inFlightMap, cacheKey, homeserverUrl, accessToken, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return await lazyTask.Value.WaitAsync(cancellationToken);
    }

    private async Task<IMatrixGateway> CreateAndCacheGatewayAsync(
        ConcurrentDictionary<string, Lazy<Task<IMatrixGateway>>> inFlightMap,
        string cacheKey,
        string homeserverUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            if (cache.TryGetValue(cacheKey, out IMatrixGateway? cachedGateway) && cachedGateway != null)
            {
                return cachedGateway;
            }

            var gateway = await CreateAuthenticatedGatewayAsync(homeserverUrl, accessToken, cancellationToken);
            cache.Set(cacheKey, gateway, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(15),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });

            return gateway;
        }
        catch (Exception ex) when (IsRateLimitResponse(ex))
        {
            throw new HomeserverRateLimitedException("Token login was rate-limited by the homeserver.", ex);
        }
        finally
        {
            inFlightMap.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>
    /// How the SDK and the HTTP stack report the homeserver's rate limit while the session is set up: a
    /// <see cref="MatrixException"/> for a Matrix error body (the SDK usually retries those itself), or an
    /// <see cref="HttpRequestException"/> with status <c>429</c> for any other body.
    /// </summary>
    private static bool IsRateLimitResponse(Exception ex) =>
        ex is MatrixException { ErrorCode: MatrixException.ErrorCodes.M_LIMIT_EXCEEDED } ||
        ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests };

    protected virtual async Task<IMatrixGateway> CreateAuthenticatedGatewayAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken)
    {
        var endpoints = await GetEndpointsAsync(homeserverUrl, cancellationToken);
        var wellKnownUris = new HomeserverResolverService.WellKnownUris
        {
            Client = endpoints.ClientUrl,
            Server = endpoints.ServerUrl
        };

        // Construct the SDK homeserver from the validated URLs instead of HomeserverProviderService,
        // which would re-run LibMatrix's unchecked .well-known resolution.
        AuthenticatedHomeserverGeneric authenticatedHomeserver =
            string.Equals(endpoints.ServerSoftwareName, GatewayConstants.SynapseBrand, StringComparison.OrdinalIgnoreCase)
                ? new AuthenticatedHomeserverSynapse(homeserverUrl, wellKnownUris, null, accessToken)
                : new AuthenticatedHomeserverGeneric(homeserverUrl, wellKnownUris, null, accessToken);
        await authenticatedHomeserver.Initialise();

        if (authenticatedHomeserver is AuthenticatedHomeserverSynapse synapse)
        {
            var synapseVersion = await ProbeSynapseVersionAsync(synapse, cancellationToken);
            if (!IsSynapseAdminCapable(synapseVersion))
            {
                return new GenericMatrixGateway(synapse);
            }

            // Switch Synapse to using SynapseAdminGateway so it is thoroughly exercised and validated
            return new SynapseAdminGateway(synapse, GatewayConstants.SynapseBrand, synapseVersion);
        }

        // 1. Try to get federation version
        var (brand, version, brandResolved, supportsAdmin) = await ProbeFederationVersionAsync(authenticatedHomeserver, cancellationToken);

        // 2. Double-check with an active probe to /_synapse/admin/v1/server_version
        // Only run the active probe if:
        // - We didn't resolve a brand (e.g. federation disabled), so we fallback to testing the endpoint
        // - Or we identified a compatible brand (Synapse >= 1.145.0 or Tuwunel >= 1.8.2) and want to make sure it's active
        if (!brandResolved || supportsAdmin)
        {
            (supportsAdmin, version) = await ProbeAdminApiAsync(authenticatedHomeserver, version, cancellationToken);
        }

        if (supportsAdmin)
        {
            // A server with known deviations from the Synapse Admin API gets its own gateway class (ADR 0006);
            // every other server, including one whose brand couldn't be resolved, gets the Synapse contract. Without a
            // brand, the Synapse minimum isn't applied: the version could be another server's numbering (#612).
            return string.Equals(brand, GatewayConstants.TuwunelBrand, StringComparison.OrdinalIgnoreCase)
                ? new TuwunelAdminGateway(authenticatedHomeserver, brand, version)
                : new SynapseAdminGateway(authenticatedHomeserver, brand, version);
        }

        return new GenericMatrixGateway(authenticatedHomeserver);
    }

    /// <summary>Reads the Synapse version from the admin API. Returns <see cref="GatewayConstants.UnknownVersion"/> if the probe fails or reports none.</summary>
    private static async Task<string> ProbeSynapseVersionAsync(AuthenticatedHomeserverSynapse synapse, CancellationToken cancellationToken)
    {
        string synapseVersion = GatewayConstants.UnknownVersion;
        try
        {
            var versionInfo = await synapse.ClientHttpClient.GetFromJsonAsync<SynapseVersionResponse>(
                GatewayConstants.ServerVersionPath, cancellationToken: cancellationToken);
            if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.ServerVersion))
            {
                synapseVersion = versionInfo.ServerVersion;
            }
        }
        // Probe failures are soft, but a cancelled request must not fall through and get cached (#391).
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Soft fail on version probe
        }

        return synapseVersion;
    }

    /// <summary>
    /// Reads brand and version from <c>/_matrix/federation/v1/version</c>. <c>SupportsAdmin</c> is set for brands known
    /// to support the Synapse Admin API (Synapse >= 1.145.0, Tuwunel >= 1.8.2); the active probe then confirms it.
    /// </summary>
    private static async Task<(string Brand, string Version, bool BrandResolved, bool SupportsAdmin)> ProbeFederationVersionAsync(
        AuthenticatedHomeserverGeneric homeserver, CancellationToken cancellationToken)
    {
        string brand = GatewayConstants.GenericBrand;
        string version = GatewayConstants.UnknownVersion;
        bool supportsAdmin = false;

        bool brandResolved = false;

        try
        {
            var federationVersion = await homeserver.ClientHttpClient.GetFromJsonAsync<LibMatrix.Responses.Federation.ServerVersionResponse>(
                "/_matrix/federation/v1/version", null, cancellationToken);
            if (federationVersion?.Server != null)
            {
                brand = federationVersion.Server.Name ?? GatewayConstants.GenericBrand;
                version = federationVersion.Server.Version ?? GatewayConstants.UnknownVersion;
                brandResolved = true;

                // If it is known to support Synapse Admin API
                if (brand.Equals(GatewayConstants.TuwunelBrand, StringComparison.OrdinalIgnoreCase))
                {
                    supportsAdmin = IsTuwunelAdminCapable(version);
                }
                else if (brand.Equals(GatewayConstants.SynapseBrand, StringComparison.OrdinalIgnoreCase))
                {
                    supportsAdmin = IsSynapseAdminCapable(version);
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Soft fail on federation version probe
        }

        return (brand, version, brandResolved, supportsAdmin);
    }

    /// <summary>
    /// Active probe of <c>/_synapse/admin/v1/server_version</c>. A success status means the admin API is available; if
    /// the body parses, its <c>server_version</c> replaces <paramref name="version"/>, even when empty. Any other
    /// status or failure means no admin API.
    /// </summary>
    private static async Task<(bool SupportsAdmin, string Version)> ProbeAdminApiAsync(
        AuthenticatedHomeserverGeneric homeserver, string version, CancellationToken cancellationToken)
    {
        bool supportsAdmin;
        try
        {
            using var response = await homeserver.ClientHttpClient.GetAsync(GatewayConstants.ServerVersionPath, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                supportsAdmin = true;

                try
                {
                    var versionInfo = await response.Content.ReadFromJsonAsync<SynapseVersionResponse>(cancellationToken: cancellationToken);
                    if (versionInfo != null)
                    {
                        version = versionInfo.ServerVersion;
                    }
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
            }
            else
            {
                supportsAdmin = false;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            supportsAdmin = false;
        }

        return (supportsAdmin, version);
    }

    /// <summary>
    /// Whether a Tuwunel version is new enough for the Synapse Admin API (>= 1.8.2: 1.8.1 doesn't serve seven endpoints
    /// the app calls). A suffix after the first <c>-</c> is dropped, except when the dash is the first character. An
    /// unparseable version counts as capable, so the active probe decides.
    /// </summary>
    internal static bool IsTuwunelAdminCapable(string version)
    {
        bool supportsAdmin = false;
        var cleanVersion = version;
        var dashIdx = cleanVersion.IndexOf('-');
        if (dashIdx > 0)
        {
            cleanVersion = cleanVersion.Substring(0, dashIdx);
        }

        if (System.Version.TryParse(cleanVersion, out var parsedVersion))
        {
            if (parsedVersion >= new System.Version(1, 8, 2))
            {
                supportsAdmin = true;
            }
        }
        else
        {
            // If version format is unknown or couldn't be parsed, let the active probe verify support
            supportsAdmin = true;
        }

        return supportsAdmin;
    }

    /// <summary>
    /// Whether a Synapse version is new enough for the app's admin pages (>= 1.145.0). Only the release number at the
    /// start counts, so a pre-release or build suffix of 1.145.0 is enough; a missing patch number counts as <c>0</c>.
    /// A version without a release number at the start (<see cref="GatewayConstants.UnknownVersion"/> when the probe
    /// failed, or an empty one) counts as capable, as for Tuwunel, so the admin API is not lost to a missing version.
    /// </summary>
    internal static bool IsSynapseAdminCapable(string version)
    {
        var match = SynapseReleasePattern.Match(version);
        if (!match.Success)
        {
            return true;
        }

        if (!int.TryParse(match.Groups[1].Value, out var major) ||
            !int.TryParse(match.Groups[2].Value, out var minor) ||
            !int.TryParse(match.Groups[3].Success ? match.Groups[3].Value : "0", out var patch))
        {
            // Digits too long for an int aren't a real release number either.
            return true;
        }

        return new Version(major, minor, patch) >= MinimumSynapseVersion;
    }
}
