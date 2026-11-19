using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

public class MediaService(IMatrixSessionService sessionService, ILogger<MediaService> logger, IStringLocalizer<SharedResources> L) : IMediaService
{
    public Task<OperationResult<MediaDownload>> OpenMediaAsync(string mxc, MediaByteRange? range = null, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingMedia",
            ex => logger.LogError(ex, "Error fetching media for MXC: {Mxc}", mxc.SanitizeForLogging()),
            async gateway =>
            {
                if (string.IsNullOrWhiteSpace(mxc)) return OperationResult<MediaDownload>.Failure(L["ErrorFetchingMedia"]);
                var media = await gateway.OpenMediaAsync(mxc, range, token);
                return OperationResult<MediaDownload>.Ok(media);
            });

    public Task<OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>> GetMediaMetadataAsync(string mxc, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingMedia",
            ex => logger.LogError(ex, "Error fetching media metadata for MXC: {Mxc}", mxc.SanitizeForLogging()),
            async gateway =>
            {
                if (string.IsNullOrWhiteSpace(mxc)) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);
                var meta = await gateway.GetMediaMetadataAsync(mxc, token);
                if (meta == null) return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Failure(L["ErrorFetchingMedia"]);
                return OperationResult<SynapseAdminMediaMetadataResponse.MediaInfo>.Ok(meta);
            });

    public Task<OperationResult> QuarantineMediaAsync(string mxc, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorQuarantiningMedia",
            ex => logger.LogError(ex, "Error quarantining media {Mxc}", mxc.SanitizeForLogging()),
            async gateway =>
            {
                if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorQuarantiningMedia"]);
                await gateway.QuarantineMediaAsync(mxc, token);
                logger.LogInformation("Successfully quarantined media {Mxc}", mxc.SanitizeForLogging());
                return OperationResult.Ok(L["MediaQuarantinedSuccessfully"]);
            });

    public Task<OperationResult> UnquarantineMediaAsync(string mxc, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorUnquarantiningMedia",
            ex => logger.LogError(ex, "Error unquarantining media {Mxc}", mxc.SanitizeForLogging()),
            async gateway =>
            {
                if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorUnquarantiningMedia"]);
                await gateway.UnquarantineMediaAsync(mxc, token);
                logger.LogInformation("Successfully unquarantined media {Mxc}", mxc.SanitizeForLogging());
                return OperationResult.Ok(L["MediaUnquarantinedSuccessfully"]);
            });

    public Task<OperationResult> DeleteMediaAsync(string mxc, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingMedia",
            ex => logger.LogError(ex, "Error deleting media {Mxc}", mxc.SanitizeForLogging()),
            async gateway =>
            {
                if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorDeletingMedia"]);
                await gateway.DeleteMediaAsync(mxc, token);
                logger.LogInformation("Successfully deleted media {Mxc}", mxc.SanitizeForLogging());
                return OperationResult.Ok(L["MediaDeletedSuccessfully"]);
            });

    public Task<OperationResult> ProtectMediaAsync(string mxc, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorProtectingMedia",
            ex => logger.LogError(ex, "Error protecting media {Mxc}", mxc.SanitizeForLogging()),
            async gateway =>
            {
                if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorProtectingMedia"]);
                await gateway.ProtectMediaAsync(mxc, token);
                logger.LogInformation("Successfully protected media {Mxc}", mxc.SanitizeForLogging());
                return OperationResult.Ok(L["MediaProtectedSuccessfully"]);
            });

    public Task<OperationResult> UnprotectMediaAsync(string mxc, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorUnprotectingMedia",
            ex => logger.LogError(ex, "Error unprotecting media {Mxc}", mxc.SanitizeForLogging()),
            async gateway =>
            {
                if (string.IsNullOrWhiteSpace(mxc)) return OperationResult.Failure(L["ErrorUnprotectingMedia"]);
                await gateway.UnprotectMediaAsync(mxc, token);
                logger.LogInformation("Successfully unprotected media {Mxc}", mxc.SanitizeForLogging());
                return OperationResult.Ok(L["MediaUnprotectedSuccessfully"]);
            });

    public Task<OperationResult<SynapseAdminPurgeMediaCacheResponse>> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorPurgingMediaCache",
            ex => logger.LogError(ex, "Error purging remote media cache (beforeTs: {BeforeTs})", beforeTs),
            async gateway =>
            {
                var result = await gateway.PurgeRemoteMediaCacheAsync(beforeTs, token);
                if (result == null) return OperationResult<SynapseAdminPurgeMediaCacheResponse>.Failure(L["ErrorPurgingMediaCache"]);
                return OperationResult<SynapseAdminPurgeMediaCacheResponse>.Ok(result, string.Format(L["PurgedCacheSuccess"], result.Deleted));
            });

    public Task<OperationResult<SynapseAdminDeleteMediaResponse>> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingLocalMedia",
            ex => logger.LogError(ex, "Error deleting local media (beforeTs: {BeforeTs}, sizeGt: {SizeGt}, keepProfiles: {KeepProfiles})", beforeTs, sizeGt, keepProfiles),
            async gateway =>
            {
                var result = await gateway.DeleteLocalMediaAsync(beforeTs, sizeGt, keepProfiles, token);
                if (result == null) return OperationResult<SynapseAdminDeleteMediaResponse>.Failure(L["ErrorDeletingLocalMedia"]);
                return OperationResult<SynapseAdminDeleteMediaResponse>.Ok(result, L["DeletedLocalMediaSuccess"]);
            });
}
