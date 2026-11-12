using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using Microsoft.Extensions.Caching.Memory;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;

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
        finally
        {
            inFlightMap.TryRemove(cacheKey, out _);
        }
    }

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
            string.Equals(endpoints.ServerSoftwareName, "Synapse", StringComparison.OrdinalIgnoreCase)
                ? new AuthenticatedHomeserverSynapse(homeserverUrl, wellKnownUris, null, accessToken)
                : new AuthenticatedHomeserverGeneric(homeserverUrl, wellKnownUris, null, accessToken);
        await authenticatedHomeserver.Initialise();
        
        if (authenticatedHomeserver is AuthenticatedHomeserverSynapse synapse)
        {
            string synapseVersion = "Unknown";
            try
            {
                var versionInfo = await synapse.ClientHttpClient.GetFromJsonAsync<SynapseVersionResponse>(
                    "/_synapse/admin/v1/server_version", cancellationToken: cancellationToken);
                if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.ServerVersion))
                {
                    synapseVersion = versionInfo.ServerVersion;
                }
            }
            catch
            {
                // Soft fail on version probe
            }

            // Switch Synapse to using SynapseCompatibleAdminGateway so it is thoroughly exercised and validated
            return new SynapseCompatibleAdminGateway(synapse, "Synapse", synapseVersion);
        }
        
        if (authenticatedHomeserver is AuthenticatedHomeserverGeneric generic)
        {
            string brand = "Generic";
            string version = "Unknown";
            bool supportsAdmin = false;

            bool brandResolved = false;

            // 1. Try to get federation version
            try
            {
                var federationVersion = await generic.ClientHttpClient.GetFromJsonAsync<LibMatrix.Responses.Federation.ServerVersionResponse>(
                    "/_matrix/federation/v1/version", null, cancellationToken);
                if (federationVersion?.Server != null)
                {
                    brand = federationVersion.Server.Name ?? "Generic";
                    version = federationVersion.Server.Version ?? "Unknown";
                    brandResolved = true;

                    // If it is known to support Synapse Admin API
                    if (brand.Equals("tuwunel", StringComparison.OrdinalIgnoreCase))
                    {
                        var cleanVersion = version;
                        var dashIdx = cleanVersion.IndexOf('-');
                        if (dashIdx > 0)
                        {
                            cleanVersion = cleanVersion.Substring(0, dashIdx);
                        }

                        if (System.Version.TryParse(cleanVersion, out var parsedVersion))
                        {
                            if (parsedVersion >= new System.Version(1, 8, 1))
                            {
                                supportsAdmin = true;
                            }
                        }
                        else
                        {
                            // If version format is unknown or couldn't be parsed, let the active probe verify support
                            supportsAdmin = true;
                        }
                    }
                    else if (brand.Equals("synapse", StringComparison.OrdinalIgnoreCase))
                    {
                        supportsAdmin = true;
                    }
                }
            }
            catch
            {
                // Soft fail on federation version probe
            }

            // 2. Double-check with an active probe to /_synapse/admin/v1/server_version
            // Only run the active probe if:
            // - We didn't resolve a brand (e.g. federation disabled), so we fallback to testing the endpoint
            // - Or we identified a compatible brand (Synapse or Tuwunel >= 1.8.1) and want to make sure it's active
            if (!brandResolved || supportsAdmin)
            {
                try
                {
                    using var response = await generic.ClientHttpClient.GetAsync("/_synapse/admin/v1/server_version", cancellationToken);
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
                        catch { }
                    }
                    else
                    {
                        supportsAdmin = false;
                    }
                }
                catch
                {
                    supportsAdmin = false;
                }
            }

            if (supportsAdmin)
            {
                return new SynapseCompatibleAdminGateway(generic, brand, version);
            }
        }
        
        return new GenericMatrixGateway(authenticatedHomeserver);
    }
}
