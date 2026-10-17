using Microsoft.Extensions.Localization;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

public class BackgroundUpdatesService(
    IMatrixSessionService sessionService,
    ILogger<BackgroundUpdatesService> logger,
    IStringLocalizer<SharedResources> L) : IBackgroundUpdatesService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<BackgroundUpdatesStatusViewModel>> GetStatusAsync(CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<BackgroundUpdatesStatusViewModel>.Failure(L["NotAuthenticated"]);
        try
        {
            var status = await Gateway.GetBackgroundUpdatesStatusAsync(token);
            if (status == null) return OperationResult<BackgroundUpdatesStatusViewModel>.Failure(L["ErrorFetchingBackgroundUpdatesStatus"]);
            var vm = new BackgroundUpdatesStatusViewModel
            {
                Enabled = status.Enabled,
                CurrentUpdates = status.CurrentUpdates?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new CurrentBackgroundUpdateViewModel
                    {
                        Name = kvp.Value.Name,
                        TotalItemCount = kvp.Value.TotalItemCount,
                        TotalDurationMs = kvp.Value.TotalDurationMs,
                        AverageItemsPerMs = kvp.Value.AverageItemsPerMs
                    }) ?? []
            };
            return OperationResult<BackgroundUpdatesStatusViewModel>.Ok(vm);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<BackgroundUpdatesStatusViewModel>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching background updates status");
            return OperationResult<BackgroundUpdatesStatusViewModel>.Failure(L["ErrorFetchingBackgroundUpdatesStatus"]);
        }
    }

    public async Task<OperationResult<bool>> SetEnabledAsync(bool enabled, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<bool>.Failure(L["NotAuthenticated"]);
        try
        {
            var result = await Gateway.SetBackgroundUpdatesEnabledAsync(enabled, token);
            if (result == null) return OperationResult<bool>.Failure(L["ErrorTogglingBackgroundUpdates"]);
            var msg = $"{L["BackgroundUpdatesStatus"]}: {(result.Enabled ? L["Active"] : L["Paused"])}";
            return OperationResult<bool>.Ok(result.Enabled, msg);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<bool>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error toggling background updates enabled state to {Enabled}", enabled);
            return OperationResult<bool>.Failure(L["ErrorTogglingBackgroundUpdates"]);
        }
    }

    public async Task<OperationResult> StartJobAsync(string jobName, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await Gateway.StartBackgroundUpdatesJobAsync(jobName, token);
            return OperationResult.Ok(L["BackgroundUpdateJobStarted"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting background updates job {JobName}", jobName);
            return OperationResult.Failure(L["ErrorStartingBackgroundUpdateJob"]);
        }
    }
}
