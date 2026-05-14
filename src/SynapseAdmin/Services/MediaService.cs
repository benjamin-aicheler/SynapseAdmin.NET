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
        try
        {
            // We need to parse serverName and mediaId from MXC if the Gateway only takes those.
            // But Gateway also has GetMediaMetadataAsync(string mxcUri).
            // Let's check IMatrixGateway.
            
            // For now, let's assume we can use a helper or the Gateway has a method.
            // Actually, IMatrixGateway has QuarantineMediaAsync(string serverName, string mediaId).
            // I should probably add QuarantineMediaAsync(string mxcUri) to the gateway to be consistent.
            
            // Or just parse it here using a simple local logic to avoid LibMatrix dependency in service.
            var parts = mxc.Replace("mxc://", "").Split('/');
            if (parts.Length != 2) return OperationResult.Failure(L["ErrorQuarantiningMedia"]);

            await Gateway.QuarantineMediaAsync(parts[0], parts[1], token);
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
        try
        {
            var parts = mxc.Replace("mxc://", "").Split('/');
            if (parts.Length != 2) return OperationResult.Failure(L["ErrorUnquarantiningMedia"]);

            await Gateway.UnquarantineMediaAsync(parts[0], parts[1], token);
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
        try
        {
            var parts = mxc.Replace("mxc://", "").Split('/');
            if (parts.Length != 2) return OperationResult.Failure(L["ErrorDeletingMedia"]);

            await Gateway.DeleteMediaAsync(parts[0], parts[1], token);
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
}
