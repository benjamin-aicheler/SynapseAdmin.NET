using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using LibMatrix.StructuredData;
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
        // Avatars are small images, we can safely assume image/jpeg for the proxy
        // as we only use this for the fallback if base64 embedding failed.
        return await Preview(mxc, "image/jpeg");
    }

    [HttpGet]
    public async Task<IActionResult> Download(string mxc)
    {
        if (string.IsNullOrWhiteSpace(mxc)) return BadRequest();
        
        logger.LogInformation("Download request for MXC {Mxc} from user {UserId}", 
            mxc.SanitizeForLogging(), UserId.SanitizeForLogging());

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

        var mxcUri = MxcUri.Parse(mxc);
        var fileName = mxcUri.MediaId;
        
        return File(result.Data, "application/octet-stream", fileName);
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

        // Default to image/jpeg if no mime type is provided, but browser usually auto-detects from stream
        return File(result.Data, mimeType ?? "image/jpeg");
    }
}
