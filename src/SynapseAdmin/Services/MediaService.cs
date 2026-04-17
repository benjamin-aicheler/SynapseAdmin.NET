using LibMatrix.Homeservers;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

public class MediaService(IMatrixSessionService sessionService, ILogger<MediaService> logger, IStringLocalizer<SharedResources> L) : IMediaService
{
    private AuthenticatedHomeserverGeneric? AuthenticatedHomeserver => sessionService.AuthenticatedHomeserver;
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

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

    public async Task<OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>> GetMediaMetadataAsync(string mxc)
    {
        if (SynapseAdmin == null) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);

        try
        {
            var mxcUri = LibMatrix.StructuredData.MxcUri.Parse(mxc);
            var meta = await SynapseAdmin.GetMediaMetadataAsync(mxcUri);
            if (meta == null) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);
            return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Ok(meta);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching media metadata for MXC: {Mxc}", mxc.SanitizeForLogging());
            return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);
        }
    }
}
