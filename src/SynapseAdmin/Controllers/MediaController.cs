using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
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

    // Matrix media URIs (mxc://<server>/<media_id>) are immutable in the Matrix specification:
    // a media ID never changes its underlying content once uploaded (it can only be deleted or quarantined).
    // The ETag is therefore derived from the normalized mxc URI. Because media can still disappear, a 304 is only
    // returned after the session is authorized and the media is confirmed to exist (see OpenAuthorizedMediaAsync).
    private static EntityTagHeaderValue ComputeETag(string mxc)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mxc.Trim())))[..32].ToLowerInvariant();
        return new EntityTagHeaderValue($"\"{hash}\"");
    }

    // Protected media may be deleted, quarantined or become inaccessible at any time, so browsers must
    // revalidate on every use. The ETag still saves the body transfer when the media is unchanged.
    private const string ProtectedMediaCacheControl = "private, no-cache";

    private readonly record struct OpenedMedia(IActionResult? EarlyResult, MediaDownload? Media, EntityTagHeaderValue ETag);

    /// <summary>
    /// Authorizes the request, establishes that the media still exists, and only then evaluates
    /// <c>If-None-Match</c> (#370). Returns either an early result (401 / 404 / 304 / 416) or the open download.
    /// Opening the download costs only the upstream response headers, so a 304 releases it without
    /// transferring the body. A single byte range is forwarded to the homeserver (#372).
    /// </summary>
    private async Task<OpenedMedia> OpenAuthorizedMediaAsync(string mxc, string operation, CancellationToken cancellationToken)
    {
        var entityTag = ComputeETag(mxc);

        if (string.IsNullOrEmpty(Homeserver) || string.IsNullOrEmpty(AccessToken))
        {
            logger.LogWarning("{Operation} failed for MXC {Mxc}: Missing session information for user {UserId}",
                operation, mxc.SanitizeForLogging(), UserId.SanitizeForLogging());
            return new OpenedMedia(Unauthorized(), null, entityTag);
        }

        Response.Headers.CacheControl = ProtectedMediaCacheControl;

        var restoreResult = await sessionService.RestoreSessionAsync(Homeserver, AccessToken);
        if (!restoreResult.Success)
        {
            logger.LogWarning("{Operation} failed for MXC {Mxc}: Failed to restore session for user {UserId}",
                operation, mxc.SanitizeForLogging(), UserId.SanitizeForLogging());
            return new OpenedMedia(Unauthorized(), null, entityTag);
        }

        var result = await mediaService.OpenMediaAsync(mxc, GetForwardableRange(entityTag), cancellationToken);
        if (!result.Success || result.Data == null)
        {
            logger.LogWarning("Media {Operation} not found for MXC {Mxc}", operation, mxc.SanitizeForLogging());
            return new OpenedMedia(NotFound(), null, entityTag);
        }

        var media = result.Data;

        // If-None-Match takes precedence over Range (RFC 9110, section 13.2.2).
        var ifNoneMatch = Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch != null && (ifNoneMatch.Contains(EntityTagHeaderValue.Any) || ifNoneMatch.Contains(entityTag)))
        {
            await media.DisposeAsync();
            Response.GetTypedHeaders().ETag = entityTag;
            return new OpenedMedia(StatusCode(StatusCodes.Status304NotModified), null, entityTag);
        }

        if (media.Status == MediaDownloadStatus.RangeNotSatisfiable)
        {
            await media.DisposeAsync();
            if (media.CompleteLength is { } length)
                Response.GetTypedHeaders().ContentRange = new ContentRangeHeaderValue(length);
            return new OpenedMedia(StatusCode(StatusCodes.Status416RangeNotSatisfiable), null, entityTag);
        }

        return new OpenedMedia(null, media, entityTag);
    }

    /// <summary>
    /// The byte range to forward upstream: a single <c>bytes</c> range whose <c>If-Range</c> validator (if any)
    /// matches our ETag. Anything else is not forwarded, and the full object is served, which RFC 9110 permits.
    /// </summary>
    private MediaByteRange? GetForwardableRange(EntityTagHeaderValue entityTag)
    {
        var headers = Request.GetTypedHeaders();
        var range = headers.Range;
        if (range == null || !range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase) || range.Ranges.Count != 1)
            return null;

        // No Last-Modified is sent, so a date validator can never match; an entity tag must match strongly.
        var ifRange = headers.IfRange;
        if (ifRange != null && (ifRange.EntityTag == null || !ifRange.EntityTag.Compare(entityTag, useStrongComparison: true)))
            return null;

        var item = range.Ranges.Single();
        return item.From == null && item.To == null ? null : new MediaByteRange(item.From, item.To);
    }

    /// <summary>
    /// Relays an upstream <c>206</c> as is; otherwise serves the full object. A seekable stream (in-memory callers)
    /// still gets local range processing.
    /// </summary>
    private IActionResult MediaResult(MediaDownload media, string contentType, EntityTagHeaderValue entityTag, string? downloadFileName)
    {
        if (media.Status == MediaDownloadStatus.Partial)
            return new PartialMediaResult(media, contentType, entityTag, downloadFileName);

        return File(
            media.Content,
            contentType,
            downloadFileName,
            lastModified: null,
            entityTag: entityTag,
            enableRangeProcessing: media.Content.CanSeek);
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

        var (earlyResult, media, entityTag) = await OpenAuthorizedMediaAsync(mxc, "Download", cancellationToken);
        if (earlyResult != null) return earlyResult;

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

        // Deliberately repeats the global middleware header: user-uploaded media must stay protected even if
        // Program.cs changes. Indexer assignment replaces the value, so only one header is sent (H-16, #396).
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        
        return MediaResult(media!, finalMimeType, entityTag, finalFileName);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(string mxc, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mxc)) return BadRequest();

        // Sanitize parameters
        var safeMimeType = mimeType?.Split(';')[0].Trim().ToLowerInvariant();

        logger.LogDebug("Preview request for MXC {Mxc} (mime: {MimeType}) from user {UserId}", 
            mxc.SanitizeForLogging(), safeMimeType.SanitizeForLogging(), UserId.SanitizeForLogging());

        var (earlyResult, media, entityTag) = await OpenAuthorizedMediaAsync(mxc, "Preview", cancellationToken);
        if (earlyResult != null) return earlyResult;

        // If no MIME type was provided, try to get it from metadata
        if (string.IsNullOrEmpty(safeMimeType))
        {
            var metaResult = await mediaService.GetMediaMetadataAsync(mxc, cancellationToken);
            if (metaResult.Success && metaResult.Data != null && !string.IsNullOrEmpty(metaResult.Data.MediaType))
            {
                safeMimeType = metaResult.Data.MediaType.Split(';')[0].Trim().ToLowerInvariant();
            }
        }

        // Add defensive headers for content-sniffing prevention.
        // Deliberately repeats the global middleware header: user-uploaded media must stay protected even if
        // Program.cs changes. Indexer assignment replaces the value, so only one header is sent (H-16, #396).
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        var finalMimeType = safeMimeType ?? "image/jpeg";

        // Validate previewable MIME types (images, videos, audio).
        // Disallow active web content (HTML, SVG, Javascript) from unconfined inline execution.
        bool isSafeInlineType = finalMimeType.StartsWith("image/") ||
                                finalMimeType.StartsWith("video/") ||
                                finalMimeType.StartsWith("audio/");

        if (finalMimeType == "image/svg+xml" || finalMimeType == "text/html" || !isSafeInlineType)
        {
            // For SVG or any non-standard preview type, enforce an isolated sandbox CSP
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        }

        return MediaResult(media!, finalMimeType, entityTag, downloadFileName: null);
    }
}
