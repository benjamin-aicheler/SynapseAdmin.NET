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
            return new SynapseAdminGateway(synapse);
        }
        
        // For now we only support Synapse, but we could return a generic gateway here
        return new GenericMatrixGateway(authenticatedHomeserver);
    }
}
