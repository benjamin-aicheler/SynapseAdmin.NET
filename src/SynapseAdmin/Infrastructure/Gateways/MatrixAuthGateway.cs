using LibMatrix.Homeservers;
using LibMatrix.Services;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Implementation of IMatrixAuthGateway using LibMatrix's HomeserverProviderService.
/// Centralizes unauthenticated "wire-level" entry points.
/// </summary>
public class MatrixAuthGateway(HomeserverProviderService hsProvider) : IMatrixAuthGateway
{
    public async Task ResolveHomeserverAsync(string homeserverUrl, CancellationToken cancellationToken = default)
    {
        // Matches MatrixSessionService logic: enableServer: true for federation support
        await hsProvider.GetRemoteHomeserver(homeserverUrl, enableServer: true);
    }

    public async Task<LoginResponse> LoginAsync(string homeserverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var sdkResp = await hsProvider.Login(homeserverUrl, username, password);
        return new LoginResponse
        {
            AccessToken = sdkResp.AccessToken,
            DeviceId = sdkResp.DeviceId,
            Homeserver = sdkResp.Homeserver,
            UserId = sdkResp.UserId
        };
    }

    public async Task<IMatrixGateway> GetAuthenticatedAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken = default)
    {
        var authenticatedHomeserver = await hsProvider.GetAuthenticatedWithToken(homeserverUrl, accessToken);
        
        if (authenticatedHomeserver is AuthenticatedHomeserverSynapse synapse)
        {
            // Switch Synapse to using SynapseCompatibleAdminGateway so it is thoroughly exercised and validated
            return new SynapseCompatibleAdminGateway(synapse, "Synapse", "Unknown");
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
                    var response = await generic.ClientHttpClient.GetAsync("/_synapse/admin/v1/server_version", cancellationToken);
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
