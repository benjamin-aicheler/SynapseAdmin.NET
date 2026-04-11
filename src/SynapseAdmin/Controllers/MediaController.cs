using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LibMatrix.StructuredData;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Controllers;

[Route("[controller]/[action]")]
[Authorize]
public class MediaController(IMediaService mediaService, IMatrixSessionService sessionService) : Controller
{
    private string? Homeserver => User.FindFirst("Homeserver")?.Value;
    private string? AccessToken => User.FindFirst("AccessToken")?.Value;

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
        if (string.IsNullOrEmpty(Homeserver) || string.IsNullOrEmpty(AccessToken)) return Unauthorized();

        await sessionService.RestoreSessionAsync(Homeserver, AccessToken);

        var result = await mediaService.GetMediaStreamAsync(mxc);
        if (!result.Success || result.Data == null) return NotFound();

        var mxcUri = MxcUri.Parse(mxc);
        var fileName = mxcUri.MediaId;
        
        return File(result.Data, "application/octet-stream", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(string mxc, string? mimeType = null)
    {
        if (string.IsNullOrWhiteSpace(mxc)) return BadRequest();
        if (string.IsNullOrEmpty(Homeserver) || string.IsNullOrEmpty(AccessToken)) return Unauthorized();

        await sessionService.RestoreSessionAsync(Homeserver, AccessToken);

        var result = await mediaService.GetMediaStreamAsync(mxc);
        if (!result.Success || result.Data == null) return NotFound();

        // Default to image/jpeg if no mime type is provided, but browser usually auto-detects from stream
        return File(result.Data, mimeType ?? "image/jpeg");
    }
}
