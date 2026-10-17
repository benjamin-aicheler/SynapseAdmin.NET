using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IBackgroundUpdatesService
{
    Task<OperationResult<BackgroundUpdatesStatusViewModel>> GetStatusAsync(CancellationToken token = default);
    Task<OperationResult<bool>> SetEnabledAsync(bool enabled, CancellationToken token = default);
    Task<OperationResult> StartJobAsync(string jobName, CancellationToken token = default);
}
