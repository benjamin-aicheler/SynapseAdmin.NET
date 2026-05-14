using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using System.Security.Claims;

namespace SynapseAdmin.Controllers;

[Route("[controller]/[action]")]
[Authorize]
public class MediaController(IMediaService mediaService, IMatrixSessionService sessionService, ILogger<MediaController> logger) : Controller
{
    private string? Homeserver => User.FindFirst("Homeserver")?.Value;
    private string? AccessToken => User.FindFirst("AccessToken")?.Value;
    private string? UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<IActionResult> Avatar(string mxc)
    {
        return await Preview(mxc);
    }

    [HttpGet]
    public async Task<IActionResult> Download(string mxc, string? filename = null, string? mimeType = null)
    {
        if (string.IsNullOrWhiteSpace(mxc)) return BadRequest();
        
        // Sanitize parameters
        var safeFilename = !string.IsNullOrEmpty(filename) ? Path.GetFileName(filename) : null;
        var safeMimeType = mimeType?.Split(';')[0].Trim(); // Only take the main type, ignore params/injections

        logger.LogInformation("Download request for MXC {Mxc} (file: {Filename}, mime: {MimeType}) from user {UserId}", 
            mxc.SanitizeForLogging(), safeFilename.SanitizeForLogging(), safeMimeType.SanitizeForLogging(), UserId.SanitizeForLogging());

        if (string.IsNullOrEmpty(Homeserver) || string.IsNullOrEmpty(AccessToken))
        {
            logger.LogWarning("Download failed for MXC {Mxc}: Missing session information for user {UserId}", 
                mxc.SanitizeForLogging(), UserId.SanitizeForLogging());
            return Unauthorized();
        }

        await sessionService.RestoreSessionAsync(Homeserver, AccessToken);

        var result = await mediaService.GetMediaStreamAsync(mxc);
        if (!result.Success || result.Data == null)
        {
            logger.LogWarning("Media download not found for MXC {Mxc}", mxc.SanitizeForLogging());
            return NotFound();
        }

        var mediaId = Infrastructure.Helpers.MediaHelper.GetMediaIdFromMxc(mxc);
        
        // If no MIME type was provided, try to get it from metadata
        if (string.IsNullOrEmpty(safeMimeType))
        {
            var metaResult = await mediaService.GetMediaMetadataAsync(mxc);
            if (metaResult.Success && metaResult.Data != null)
            {
                safeMimeType = metaResult.Data.MediaType;
            }
        }

        var finalFileName = safeFilename ?? mediaId;
        var finalMimeType = safeMimeType ?? "application/octet-stream";
        
        return File(result.Data, finalMimeType, finalFileName);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(string mxc, string? mimeType = null)
    {
        if (string.IsNullOrWhiteSpace(mxc)) return BadRequest();

        logger.LogDebug("Preview request for MXC {Mxc} (mime: {MimeType}) from user {UserId}", 
            mxc.SanitizeForLogging(), mimeType.SanitizeForLogging(), UserId.SanitizeForLogging());

        if (string.IsNullOrEmpty(Homeserver) || string.IsNullOrEmpty(AccessToken))
        {
            logger.LogWarning("Preview failed for MXC {Mxc}: Missing session information for user {UserId}", 
                mxc.SanitizeForLogging(), UserId.SanitizeForLogging());
            return Unauthorized();
        }

        await sessionService.RestoreSessionAsync(Homeserver, AccessToken);

        var result = await mediaService.GetMediaStreamAsync(mxc);
        if (!result.Success || result.Data == null)
        {
            logger.LogWarning("Media preview not found for MXC {Mxc}", mxc.SanitizeForLogging());
            return NotFound();
        }

        // If no MIME type was provided, try to get it from metadata
        if (string.IsNullOrEmpty(mimeType))
        {
            var metaResult = await mediaService.GetMediaMetadataAsync(mxc);
            if (metaResult.Success && metaResult.Data != null)
            {
                mimeType = metaResult.Data.MediaType;
            }
        }

        // Default to image/jpeg if still unknown, but browser usually auto-detects from stream
        return File(result.Data, mimeType ?? "image/jpeg");
    }
}
