using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Interfaces.Gateways;

namespace SynapseAdmin.Services;

public class MediaService(IMatrixSessionService sessionService, ILogger<MediaService> logger, IStringLocalizer<SharedResources> L) : IMediaService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<Stream>> GetMediaStreamAsync(string mxc, CancellationToken token = default)
    {
        if (!sessionService.IsLoggedIn || Gateway == null) return OperationResult<Stream>.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult<Stream>.Failure(L["ErrorFetchingMedia"]);

        try
        {
            var stream = await Gateway.GetMediaStreamAsync(mxc, cancellationToken: token);
            return OperationResult<Stream>.Ok(stream);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<Stream>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching media for MXC: {Mxc}", mxc.SanitizeForLogging());
            return OperationResult<Stream>.Failure(L["ErrorFetchingMedia"]);
        }
    }

    public async Task<OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>> GetMediaMetadataAsync(string mxc, CancellationToken token = default)
    {
        if (!sessionService.IsLoggedIn || Gateway == null) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);

        try
        {
            var meta = await Gateway.GetMediaMetadataAsync(mxc, token);
            if (meta == null) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);
            return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Ok(meta);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Cancelled();
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
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorQuarantiningMedia"]);
        try
        {
            await Gateway.QuarantineMediaAsync(mxc, token);
            logger.LogInformation("Successfully quarantined media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Ok(L["MediaQuarantinedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error quarantining media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorQuarantiningMedia"]);
        }
    }

    public async Task<OperationResult> UnquarantineMediaAsync(string mxc, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorUnquarantiningMedia"]);
        try
        {
            await Gateway.UnquarantineMediaAsync(mxc, token);
            logger.LogInformation("Successfully unquarantined media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Ok(L["MediaUnquarantinedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unquarantining media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorUnquarantiningMedia"]);
        }
    }

    public async Task<OperationResult> DeleteMediaAsync(string mxc, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorDeletingMedia"]);
        try
        {
            await Gateway.DeleteMediaAsync(mxc, token);
            logger.LogInformation("Successfully deleted media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Ok(L["MediaDeletedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorDeletingMedia"]);
        }
    }

    public async Task<OperationResult> ProtectMediaAsync(string mxc, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorProtectingMedia"]);
        try
        {
            await Gateway.ProtectMediaAsync(mxc, token);
            logger.LogInformation("Successfully protected media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Ok(L["MediaProtectedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error protecting media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorProtectingMedia"]);
        }
    }

    public async Task<OperationResult> UnprotectMediaAsync(string mxc, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorUnprotectingMedia"]);
        try
        {
            await Gateway.UnprotectMediaAsync(mxc, token);
            logger.LogInformation("Successfully unprotected media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Ok(L["MediaUnprotectedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unprotecting media {Mxc}", mxc.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorUnprotectingMedia"]);
        }
    }

    public async Task<OperationResult<SynapseAdminPurgeMediaCacheResponse>> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<SynapseAdminPurgeMediaCacheResponse>.Failure(L["NotAuthenticated"]);
        try
        {
            var result = await Gateway.PurgeRemoteMediaCacheAsync(beforeTs, token);
            if (result == null) return OperationResult<SynapseAdminPurgeMediaCacheResponse>.Failure(L["ErrorPurgingMediaCache"]);
            return OperationResult<SynapseAdminPurgeMediaCacheResponse>.Ok(result, L["PurgedCacheSuccess"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SynapseAdminPurgeMediaCacheResponse>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error purging remote media cache (beforeTs: {BeforeTs})", beforeTs);
            return OperationResult<SynapseAdminPurgeMediaCacheResponse>.Failure(L["ErrorPurgingMediaCache"]);
        }
    }

    public async Task<OperationResult<SynapseAdminDeleteMediaResponse>> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<SynapseAdminDeleteMediaResponse>.Failure(L["NotAuthenticated"]);
        try
        {
            var result = await Gateway.DeleteLocalMediaAsync(beforeTs, sizeGt, keepProfiles, token);
            if (result == null) return OperationResult<SynapseAdminDeleteMediaResponse>.Failure(L["ErrorDeletingLocalMedia"]);
            return OperationResult<SynapseAdminDeleteMediaResponse>.Ok(result, L["DeletedLocalMediaSuccess"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SynapseAdminDeleteMediaResponse>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting local media (beforeTs: {BeforeTs}, sizeGt: {SizeGt}, keepProfiles: {KeepProfiles})", beforeTs, sizeGt, keepProfiles);
            return OperationResult<SynapseAdminDeleteMediaResponse>.Failure(L["ErrorDeletingLocalMedia"]);
        }
    }
}
