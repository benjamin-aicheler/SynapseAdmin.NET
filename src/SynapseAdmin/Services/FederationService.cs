using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces.Gateways;

namespace SynapseAdmin.Services;

public class FederationService(IMatrixSessionService sessionService, ILogger<FederationService> logger, IStringLocalizer<SharedResources> L) : IFederationService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>> GetDestinationsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Failure(L["NotAuthenticated"]);

        try
        {
            var dir = direction == SortDirection.Ascending ? "f" : "b";
            var result = await Gateway.GetFederationDestinationListAsync(offset, limit, dir, token);
            if (result == null) return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Ok((0, []));
            
            var vms = result.Destinations.ToViewModels();

            return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Ok((result.Total, vms));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching federation destinations (offset: {Offset}, limit: {Limit})", offset, limit);
            return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Failure(L["ErrorFetchingFederationDestinations"]);
        }
    }

    public async Task<OperationResult> ResetConnectionTimeoutAsync(string destination, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await Gateway.ResetFederationConnectionTimeoutAsync(destination, token);
            logger.LogInformation("Successfully reset federation connection timeout for {Destination}", destination.SanitizeForLogging());
            return OperationResult.Ok(L["ResetFederationConnectionSuccessful"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resetting federation connection timeout for {Destination}", destination.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorResettingFederationConnection"]);
        }
    }
}
