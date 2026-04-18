using SynapseAdmin.Models.ViewModels;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;

namespace SynapseAdmin.Extensions.Mapping;

public static class FederationMappingExtensions
{
    public static FederationDestinationListViewModel ToViewModel(this LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses.SynapseAdminDestinationListResult.SynapseAdminDestinationListResultDestination destination)
    {
        return new FederationDestinationListViewModel
        {
            Destination = destination.Destination,
            RetryLastTs = destination.RetryLastTs,
            RetryInterval = destination.RetryInterval,
            FailureTs = destination.FailureTs,
            LastSuccessfulStreamOrdering = destination.LastSuccessfulStreamOrdering
        };
    }

    public static List<FederationDestinationListViewModel> ToViewModels(this IEnumerable<LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses.SynapseAdminDestinationListResult.SynapseAdminDestinationListResultDestination> destinations)
    {
        return destinations.Select(ToViewModel).ToList();
    }
}
