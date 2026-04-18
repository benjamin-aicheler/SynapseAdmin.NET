using LibMatrix.Homeservers;
using LibMatrix.Responses;
using LibMatrix.Services;
using SynapseAdmin.Interfaces.Gateways;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Implementation of IMatrixAuthGateway using LibMatrix's HomeserverProviderService.
/// Centralizes unauthenticated "wire-level" entry points.
/// </summary>
public class MatrixAuthGateway(HomeserverProviderService hsProvider) : IMatrixAuthGateway
{
    public async Task<RemoteHomeserver> ResolveHomeserverAsync(string homeserverUrl, CancellationToken cancellationToken = default)
    {
        // Matches MatrixSessionService logic: enableServer: true for federation support
        return await hsProvider.GetRemoteHomeserver(homeserverUrl, enableServer: true);
    }

    public async Task<LoginResponse> LoginAsync(string homeserverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        return await hsProvider.Login(homeserverUrl, username, password);
    }

    public async Task<AuthenticatedHomeserverGeneric> GetAuthenticatedAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken = default)
    {
        return await hsProvider.GetAuthenticatedWithToken(homeserverUrl, accessToken);
    }
}
