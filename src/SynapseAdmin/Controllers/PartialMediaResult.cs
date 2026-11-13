using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Controllers;

/// <summary>
/// Relays a <c>206 Partial Content</c> response that the homeserver produced for a forwarded byte range (#372).
/// The upstream stream is not seekable, so ASP.NET Core's own range processing cannot slice it; the range
/// was applied upstream and only its <c>Content-Range</c> is passed through. The download is always disposed.
/// </summary>
internal sealed class PartialMediaResult(
    MediaDownload media,
    string contentType,
    EntityTagHeaderValue entityTag,
    string? downloadFileName) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var httpContext = context.HttpContext;
        var response = httpContext.Response;

        await using (media)
        {
            var from = media.RangeFrom!.Value;
            var to = media.RangeTo!.Value;

            response.StatusCode = StatusCodes.Status206PartialContent;
            response.ContentType = contentType;
            response.ContentLength = media.ContentLength ?? to - from + 1;
            response.Headers.AcceptRanges = "bytes";

            var headers = response.GetTypedHeaders();
            headers.ETag = entityTag;
            headers.ContentRange = media.CompleteLength is { } length
                ? new ContentRangeHeaderValue(from, to, length)
                : new ContentRangeHeaderValue(from, to);

            if (!string.IsNullOrEmpty(downloadFileName))
            {
                var disposition = new ContentDispositionHeaderValue("attachment");
                disposition.SetHttpFileName(downloadFileName);
                headers.ContentDisposition = disposition;
            }

            try
            {
                await media.Content.CopyToAsync(response.Body, httpContext.RequestAborted);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client went away; disposing the download above cancels the upstream transfer.
                httpContext.Abort();
            }
        }
    }
}
