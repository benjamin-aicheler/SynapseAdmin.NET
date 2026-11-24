using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IFederationService
{
    /// <summary>
    /// Every federation destination, read page by page by following <c>next_token</c> (#551). Fails as a whole when
    /// any page fails.
    /// </summary>
    Task<OperationResult<List<FederationDestinationListViewModel>>> GetAllDestinationsAsync(CancellationToken token = default);
    Task<OperationResult> ResetConnectionTimeoutAsync(string destination, CancellationToken token = default);
}
