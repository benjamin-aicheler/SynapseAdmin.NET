using LibMatrix.Homeservers;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

public class MediaService(IMatrixSessionService sessionService, ILogger<MediaService> logger, IStringLocalizer<SharedResources> L) : IMediaService
{
    private AuthenticatedHomeserverGeneric? AuthenticatedHomeserver => sessionService.AuthenticatedHomeserver;

    public async Task<OperationResult<Stream>> GetMediaStreamAsync(string mxc)
    {
        if (AuthenticatedHomeserver == null) return OperationResult<Stream>.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult<Stream>.Failure(L["ErrorFetchingMedia"]);

        try
        {
            var stream = await AuthenticatedHomeserver.GetMediaStreamAsync(mxc);
            return OperationResult<Stream>.Ok(stream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching media for MXC: {Mxc}", mxc.SanitizeForLogging());
            return OperationResult<Stream>.Failure(L["ErrorFetchingMedia"]);
        }
    }
}
