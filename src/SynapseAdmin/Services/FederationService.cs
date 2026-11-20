using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;

namespace SynapseAdmin.Services;

public class FederationService(IMatrixSessionService sessionService, ILogger<FederationService> logger, IStringLocalizer<SharedResources> L) : IFederationService
{
    public Task<OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>> GetDestinationsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingFederationDestinations",
            ex => logger.LogError(ex, "Error fetching federation destinations (offset: {Offset}, limit: {Limit})", offset, limit),
            async gateway =>
            {
                var dir = direction.ToSynapseDir(whenUnsorted: "b");
                var result = await gateway.GetFederationDestinationListAsync(offset, limit, dir, token);
                if (result == null) return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Ok((0, []));

                var vms = result.Destinations.ToViewModels();

                return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Ok((result.Total, vms));
            });

    public Task<OperationResult> ResetConnectionTimeoutAsync(string destination, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorResettingFederationConnection",
            ex => logger.LogError(ex, "Error resetting federation connection timeout for {Destination}", destination.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.ResetFederationConnectionTimeoutAsync(destination, token);
                logger.LogInformation("Successfully reset federation connection timeout for {Destination}", destination.SanitizeForLogging());
                return OperationResult.Ok(L["ResetFederationConnectionSuccessful"]);
            });
}
