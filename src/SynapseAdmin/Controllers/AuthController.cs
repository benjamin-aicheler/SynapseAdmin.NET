using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Controllers;

[Route("[controller]/[action]")]
[EnableRateLimiting("auth")]
public class AuthController(
    ISessionBridgeService bridgeService,
    IAntiforgery antiforgery,
    IOptions<AntiforgeryOptions> antiforgeryOptions,
    ILogger<AuthController> logger) : Controller
{
    public sealed record SignInRequest(string? Key);

    /// <summary>
    /// Exchanges a session bridge key for the authentication cookie. Called via <c>fetch</c> from the Blazor page
    /// (<c>wwwroot/js/auth-bridge.js</c>), so the key travels in the request body, never in a URL.
    /// The request must carry a valid antiforgery token for this browser, and that token must be the one the
    /// bridge was created with (binding the exchange to the initiating browser).
    /// </summary>
    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest? request)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            logger.LogWarning("SignIn attempt rejected: antiforgery validation failed ({Reason}).", ex.Message.SanitizeForLogging());
            return BadRequest();
        }

        var headerName = antiforgeryOptions.Value.HeaderName;
        var bindingToken = headerName is null ? null : Request.Headers[headerName].ToString();

        if (!bridgeService.TryConsumeBridge(request?.Key, bindingToken, out var data))
        {
            logger.LogWarning("SignIn attempt failed: invalid, expired, reused or unbound session bridge key.");
            return BadRequest();
        }

        logger.LogInformation("SignIn successful for user {UserId} on {Homeserver}.",
            data.UserId.SanitizeForLogging(), data.Homeserver.SanitizeForLogging());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, data.UserId),
            new(ClaimTypes.Name, data.Username),
            new("Homeserver", data.Homeserver),
            new("AccessToken", data.AccessToken)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
        };

        if (!HttpContext.Request.IsHttps && !IsLoopbackHost(HttpContext.Request.Host.Host))
        {
            logger.LogWarning(
                "SignIn executed over plain HTTP on non-localhost host ({Host}). Browser will reject the Secure authentication cookie unless accessed via HTTPS or a reverse proxy forwarding X-Forwarded-Proto: https.",
                HttpContext.Request.Host.Value.SanitizeForLogging());
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> SignOutAction()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        logger.LogInformation("SignOut initiated for user {UserId}", userId.SanitizeForLogging());
        
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect("~/");
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
}
