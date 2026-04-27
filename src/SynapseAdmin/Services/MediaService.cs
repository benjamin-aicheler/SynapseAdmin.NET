using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Interfaces.Gateways;
using LibMatrix.StructuredData;

namespace SynapseAdmin.Services;

public class MediaService(IMatrixSessionService sessionService, ILogger<MediaService> logger, IStringLocalizer<SharedResources> L) : IMediaService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<Stream>> GetMediaStreamAsync(string mxc)
    {
        if (!sessionService.IsLoggedIn || Gateway == null) return OperationResult<Stream>.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult<Stream>.Failure(L["ErrorFetchingMedia"]);

        try
        {
            var stream = await Gateway.GetMediaStreamAsync(mxc);
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
        if (!sessionService.IsLoggedIn || Gateway == null) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);

        try
        {
            var mxcUri = MxcUri.Parse(mxc);
            var meta = await Gateway.GetMediaMetadataAsync(mxcUri);
            if (meta == null) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);
            return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Ok(meta);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching media metadata for MXC: {Mxc}", mxc.SanitizeForLogging());
            return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);
        }
    }

    public async Task<OperationResult> QuarantineMediaAsync(string mxc, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            var parsed = MxcUri.Parse(mxc);
            await Gateway.QuarantineMediaAsync(parsed.ServerName, parsed.MediaId, token);
            logger.LogInformation("Successfully quarantined media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Ok(L["MediaQuarantinedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error quarantining media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorQuarantiningMedia"]);
        }
    }

    public async Task<OperationResult> DeleteMediaAsync(string mxc, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            var parsed = MxcUri.Parse(mxc);
            await Gateway.DeleteMediaAsync(parsed.ServerName, parsed.MediaId, token);
            logger.LogInformation("Successfully deleted media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Ok(L["MediaDeletedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorDeletingMedia"]);
        }
    }
}
