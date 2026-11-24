using LibMatrix.Homeservers;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// The gateway for Tuwunel (>= 1.8.1), which serves the Synapse Admin API with known deviations (ADR 0006). It inherits
/// the Synapse contract and overrides only the methods where Tuwunel deviates, so that each returns Synapse's result.
/// <c>MatrixAuthGateway</c> picks it when the federation reports the Tuwunel brand, in any casing.
/// </summary>
internal sealed class TuwunelAdminGateway(
    AuthenticatedHomeserverGeneric homeserver,
    string serverBrand = GatewayConstants.TuwunelBrand,
    string serverVersion = GatewayConstants.UnknownVersion) : SynapseAdminGateway(homeserver, serverBrand, serverVersion)
{
    /// <summary>
    /// Callers pass the dir of Synapse's documented order. Tuwunel sorts the keys Synapse sorts largest first
    /// ascending instead, so it needs the opposite dir for the same order (#541).
    /// </summary>
    public override Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        if (RoomSortKeys.IsLargestFirst(orderBy))
        {
            direction = RoomSortKeys.Reverse(direction);
        }

        return base.GetRoomListAsync(offset, limit, orderBy, direction, searchTerm, cancellationToken);
    }
}
