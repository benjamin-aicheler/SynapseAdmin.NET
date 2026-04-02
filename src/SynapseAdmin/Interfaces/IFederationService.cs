using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IFederationService
{
    Task<OperationResult<(int Total, List<FederationDestinationListViewModel> Destinations)>> GetDestinationsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default);
    Task<OperationResult> ResetConnectionTimeoutAsync(string destination);
}
