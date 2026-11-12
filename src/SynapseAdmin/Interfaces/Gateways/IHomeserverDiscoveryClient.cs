using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Interfaces.Gateways;

/// <summary>
/// Policy-checked wire client for the unauthenticated part of the Matrix flow: homeserver discovery
/// (<c>.well-known</c> delegation) and password login. Every hop and every resolved address is validated
/// against <see cref="IHomeserverDestinationPolicy"/> before a connection is made.
/// </summary>
public interface IHomeserverDiscoveryClient
{
    /// <summary>
    /// Resolves the client (and, if reachable, federation) base URLs for a homeserver entered by the user.
    /// Throws <see cref="Models.HomeserverNotAllowedException"/> when the policy refuses a destination.
    /// </summary>
    Task<HomeserverEndpoints> DiscoverAsync(string homeserverUrl, CancellationToken cancellationToken = default);

    /// <summary>Performs an <c>m.login.password</c> login against an already discovered client base URL.</summary>
    Task<LoginResponse> LoginAsync(string clientBaseUrl, string username, string password, CancellationToken cancellationToken = default);
}
