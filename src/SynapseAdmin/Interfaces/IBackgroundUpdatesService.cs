using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Interfaces;

public interface IBackgroundUpdatesService
{
    Task<OperationResult<SynapseAdminBackgroundUpdatesStatusResponse>> GetStatusAsync(CancellationToken token = default);
    Task<OperationResult<SynapseAdminBackgroundUpdatesEnabledResponse>> SetEnabledAsync(bool enabled, CancellationToken token = default);
    Task<OperationResult> StartJobAsync(string jobName, CancellationToken token = default);
}
