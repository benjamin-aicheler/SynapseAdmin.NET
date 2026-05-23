using Microsoft.Extensions.Localization;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

public class BackgroundUpdatesService(
    IMatrixSessionService sessionService,
    ILogger<BackgroundUpdatesService> logger,
    IStringLocalizer<SharedResources> L) : IBackgroundUpdatesService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<SynapseAdminBackgroundUpdatesStatusResponse>> GetStatusAsync(CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<SynapseAdminBackgroundUpdatesStatusResponse>.Failure(L["NotAuthenticated"]);
        try
        {
            var status = await Gateway.GetBackgroundUpdatesStatusAsync(token);
            if (status == null) return OperationResult<SynapseAdminBackgroundUpdatesStatusResponse>.Failure(L["ErrorFetchingBackgroundUpdatesStatus"]);
            return OperationResult<SynapseAdminBackgroundUpdatesStatusResponse>.Ok(status);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SynapseAdminBackgroundUpdatesStatusResponse>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching background updates status");
            return OperationResult<SynapseAdminBackgroundUpdatesStatusResponse>.Failure(L["ErrorFetchingBackgroundUpdatesStatus"]);
        }
    }

    public async Task<OperationResult<SynapseAdminBackgroundUpdatesEnabledResponse>> SetEnabledAsync(bool enabled, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<SynapseAdminBackgroundUpdatesEnabledResponse>.Failure(L["NotAuthenticated"]);
        try
        {
            var result = await Gateway.SetBackgroundUpdatesEnabledAsync(enabled, token);
            if (result == null) return OperationResult<SynapseAdminBackgroundUpdatesEnabledResponse>.Failure(L["ErrorTogglingBackgroundUpdates"]);
            return OperationResult<SynapseAdminBackgroundUpdatesEnabledResponse>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SynapseAdminBackgroundUpdatesEnabledResponse>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error toggling background updates enabled state to {Enabled}", enabled);
            return OperationResult<SynapseAdminBackgroundUpdatesEnabledResponse>.Failure(L["ErrorTogglingBackgroundUpdates"]);
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
