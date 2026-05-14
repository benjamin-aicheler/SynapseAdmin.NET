using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Interfaces.Gateways;

/// <summary>
/// Defines the contract for unauthenticated Matrix operations like login and homeserver resolution.
/// This acts as a factory for producing authenticated IMatrixGateway instances.
/// </summary>
public interface IMatrixAuthGateway
{
    /// <summary>
    /// Resolves a homeserver URL, checking for federation support and server type.
    /// Throws an exception if the homeserver cannot be resolved or is invalid.
    /// </summary>
    Task ResolveHomeserverAsync(string homeserverUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a standard Matrix login.
    /// </summary>
    Task<LoginResponse> LoginAsync(string homeserverUrl, string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an authenticated homeserver session from an existing access token.
    /// </summary>
    Task<IMatrixGateway> GetAuthenticatedAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken = default);
}
