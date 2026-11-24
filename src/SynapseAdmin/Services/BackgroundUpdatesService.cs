using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

public class BackgroundUpdatesService(
    IMatrixSessionService sessionService,
    ILogger<BackgroundUpdatesService> logger,
    IStringLocalizer<SharedResources> L) : IBackgroundUpdatesService
{
    public Task<OperationResult<BackgroundUpdatesStatusViewModel>> GetStatusAsync(CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingBackgroundUpdatesStatus",
            ex => logger.LogError(ex, "Error fetching background updates status"),
            async gateway =>
            {
                var status = await gateway.GetBackgroundUpdatesStatusAsync(token);
                if (status == null) return OperationResult<BackgroundUpdatesStatusViewModel>.Failure(L["ErrorFetchingBackgroundUpdatesStatus"]);
                var vm = status.ToViewModel();
                return OperationResult<BackgroundUpdatesStatusViewModel>.Ok(vm);
            },
            // A page loads this by itself: a homeserver that refuses it works as intended, so the refusal isn't an error.
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support background updates"));

    public Task<OperationResult<bool>> SetEnabledAsync(bool enabled, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorTogglingBackgroundUpdates",
            ex => logger.LogError(ex, "Error toggling background updates enabled state to {Enabled}", enabled),
            async gateway =>
            {
                var result = await gateway.SetBackgroundUpdatesEnabledAsync(enabled, token);
                if (result == null) return OperationResult<bool>.Failure(L["ErrorTogglingBackgroundUpdates"]);
                var msg = $"{L["BackgroundUpdatesStatus"]}: {(result.Enabled ? L["Active"] : L["Paused"])}";
                return OperationResult<bool>.Ok(result.Enabled, msg);
            });

    public Task<OperationResult> StartJobAsync(string jobName, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorStartingBackgroundUpdateJob",
            ex => logger.LogError(ex, "Error starting background updates job {JobName}", jobName.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.StartBackgroundUpdatesJobAsync(jobName, token);
                return OperationResult.Ok(L["BackgroundUpdateJobStarted"]);
            });
}
