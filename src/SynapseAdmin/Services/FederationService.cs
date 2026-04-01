using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Services;

public class FederationService(MatrixSessionService sessionService)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<(int Total, List<FederationDestinationListViewModel> Destinations)> GetDestinationsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return (0, []);

        var dir = direction == SortDirection.Ascending ? "f" : "b";
        var url = $"/_synapse/admin/v1/federation/destinations?from={offset}&limit={limit}&dir={dir}";

        var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminDestinationListResult>(url, cancellationToken: token);
        if (result == null) return (0, []);
        
        var vms = result.Destinations.Select(d => new FederationDestinationListViewModel
        {
            Destination = d.Destination,
            RetryLastTs = d.RetryLastTs,
            RetryInterval = d.RetryInterval,
            FailureTs = d.FailureTs,
            LastSuccessfulStreamOrdering = d.LastSuccessfulStreamOrdering
        }).ToList();

        return (result.Total, vms);
    }

    public async Task ResetConnectionTimeoutAsync(string destination)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.ResetFederationConnectionTimeoutAsync(destination);
    }
}
