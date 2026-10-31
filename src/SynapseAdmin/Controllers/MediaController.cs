using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SynapseAdmin.Controllers;

[Route("[controller]/[action]")]
[Authorize]
public class MediaController(IMediaService mediaService, IMatrixSessionService sessionService, ILogger<MediaController> logger) : Controller
{
    private string? Homeserver => User.FindFirst("Homeserver")?.Value;
    private string? AccessToken => User.FindFirst("AccessToken")?.Value;
    private string? UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static EntityTagHeaderValue ComputeETag(string mxc)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mxc.Trim())))[..32].ToLowerInvariant();
        return new EntityTagHeaderValue($"\"{hash}\"");
    }

    [HttpGet]
    public async Task<IActionResult> Avatar(string mxc, CancellationToken cancellationToken = default)
    {
        return await Preview(mxc, cancellationToken: cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Download(string mxc, string? filename = null, string? mimeType = null, CancellationToken cancellationToken = default)
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

        var restoreResult = await sessionService.RestoreSessionAsync(Homeserver, AccessToken);
        if (!restoreResult.Success)
        {
            logger.LogWarning("Download failed for MXC {Mxc}: Failed to restore session for user {UserId}", 
                mxc.SanitizeForLogging(), UserId.SanitizeForLogging());
            return Unauthorized();
        }

        var result = await mediaService.GetMediaStreamAsync(mxc, cancellationToken);
        if (!result.Success || result.Data == null)
        {
            logger.LogWarning("Media download not found for MXC {Mxc}", mxc.SanitizeForLogging());
            return NotFound();
        }

        var mediaId = Infrastructure.Helpers.MediaHelper.GetMediaIdFromMxc(mxc);
        
        // If no MIME type was provided, try to get it from metadata
        if (string.IsNullOrEmpty(safeMimeType))
        {
            var metaResult = await mediaService.GetMediaMetadataAsync(mxc, cancellationToken);
            if (metaResult.Success && metaResult.Data != null)
            {
                safeMimeType = metaResult.Data.MediaType;
            }
        }

        var finalFileName = safeFilename ?? mediaId;
        var finalMimeType = safeMimeType ?? "application/octet-stream";

        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        
        return File(
            result.Data,
            finalMimeType,
            finalFileName,
            lastModified: null,
            entityTag: ComputeETag(mxc),
            enableRangeProcessing: result.Data.CanSeek);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(string mxc, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mxc)) return BadRequest();

        // Sanitize parameters
        var safeMimeType = mimeType?.Split(';')[0].Trim().ToLowerInvariant();

        logger.LogDebug("Preview request for MXC {Mxc} (mime: {MimeType}) from user {UserId}", 
            mxc.SanitizeForLogging(), safeMimeType.SanitizeForLogging(), UserId.SanitizeForLogging());

        if (string.IsNullOrEmpty(Homeserver) || string.IsNullOrEmpty(AccessToken))
        {
            logger.LogWarning("Preview failed for MXC {Mxc}: Missing session information for user {UserId}", 
                mxc.SanitizeForLogging(), UserId.SanitizeForLogging());
            return Unauthorized();
        }

        var entityTag = ComputeETag(mxc);
        Response.Headers.CacheControl = "private, max-age=300";

        if (string.IsNullOrEmpty(Request.Headers.Range))
        {
            var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
            if (ifNoneMatch != null && (ifNoneMatch.Contains(EntityTagHeaderValue.Any) || ifNoneMatch.Contains(entityTag)))
            {
                Response.GetTypedHeaders().ETag = entityTag;
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        var restoreResult = await sessionService.RestoreSessionAsync(Homeserver, AccessToken);
        if (!restoreResult.Success)
        {
            logger.LogWarning("Preview failed for MXC {Mxc}: Failed to restore session for user {UserId}", 
                mxc.SanitizeForLogging(), UserId.SanitizeForLogging());
            return Unauthorized();
        }

        var result = await mediaService.GetMediaStreamAsync(mxc, cancellationToken);
        if (!result.Success || result.Data == null)
        {
            logger.LogWarning("Media preview not found for MXC {Mxc}", mxc.SanitizeForLogging());
            return NotFound();
        }

        // If no MIME type was provided, try to get it from metadata
        if (string.IsNullOrEmpty(safeMimeType))
        {
            var metaResult = await mediaService.GetMediaMetadataAsync(mxc, cancellationToken);
            if (metaResult.Success && metaResult.Data != null && !string.IsNullOrEmpty(metaResult.Data.MediaType))
            {
                safeMimeType = metaResult.Data.MediaType.Split(';')[0].Trim().ToLowerInvariant();
            }
        }

        // Add defensive headers for content-sniffing prevention
        Response.Headers.Append("X-Content-Type-Options", "nosniff");

        var finalMimeType = safeMimeType ?? "image/jpeg";

        // Validate previewable MIME types (images, videos, audio).
        // Disallow active web content (HTML, SVG, Javascript) from unconfined inline execution.
        bool isSafeInlineType = finalMimeType.StartsWith("image/") ||
                                finalMimeType.StartsWith("video/") ||
                                finalMimeType.StartsWith("audio/");

        if (finalMimeType == "image/svg+xml" || finalMimeType == "text/html" || !isSafeInlineType)
        {
            // For SVG or any non-standard preview type, enforce an isolated sandbox CSP
            Response.Headers.Append("Content-Security-Policy", "default-src 'none'; style-src 'unsafe-inline'; sandbox");
        }

        return File(
            result.Data,
            finalMimeType,
            lastModified: null,
            entityTag: entityTag,
            enableRangeProcessing: result.Data.CanSeek);
    }
}
