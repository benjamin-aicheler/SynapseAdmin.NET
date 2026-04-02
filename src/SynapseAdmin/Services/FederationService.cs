using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace SynapseAdmin.Services;

public class FederationService(MatrixSessionService sessionService, ILogger<FederationService> logger, IStringLocalizer<SharedResources> L) : IFederationService
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>> GetDestinationsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Failure(L["NotAuthenticated"]);

        try
        {
            var dir = direction == SortDirection.Ascending ? "f" : "b";
            var url = $"/_synapse/admin/v1/federation/destinations?from={offset}&limit={limit}&dir={dir}";

            var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminDestinationListResult>(url, cancellationToken: token);
            if (result == null) return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Ok((0, []));
            
            var vms = result.Destinations.Select(d => new FederationDestinationListViewModel
            {
                Destination = d.Destination,
                RetryLastTs = d.RetryLastTs,
                RetryInterval = d.RetryInterval,
                FailureTs = d.FailureTs,
                LastSuccessfulStreamOrdering = d.LastSuccessfulStreamOrdering
            }).ToList();

            return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Ok((result.Total, vms));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching federation destinations (offset: {Offset}, limit: {Limit})", offset, limit);
            return OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>.Failure(string.Format(L["ErrorFetchingFederationDestinations"], ex.Message));
        }
    }

    public async Task<OperationResult> ResetConnectionTimeoutAsync(string destination)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await SynapseAdmin.Admin.ResetFederationConnectionTimeoutAsync(destination);
            logger.LogInformation("Successfully reset federation connection timeout for {Destination}", destination);
            return OperationResult.Ok(string.Format(L["ResetFederationConnectionSuccessful"], destination));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resetting federation connection timeout for {Destination}", destination);
            return OperationResult.Failure(string.Format(L["ErrorResettingFederationConnection"], destination, ex.Message));
        }
    }
}
