using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Controllers;

[Route("[controller]/[action]")]
public class AuthController(ISessionBridgeService bridgeService, ILogger<AuthController> logger) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SignIn(string key, string? redirectUri = null)
    {
        if (!bridgeService.TryConsumeBridge(key, out var data))
        {
            logger.LogWarning("SignIn attempt failed: invalid or expired session bridge key.");
            return LocalRedirect($"/login?Error=SessionBridgeFailed");
        }

        logger.LogInformation("SignIn successful for user {UserId} on {Homeserver}. Redirecting to: {RedirectUri}", 
            data.UserId.SanitizeForLogging(), data.Homeserver.SanitizeForLogging(), redirectUri.SanitizeForLogging());

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

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        if (string.IsNullOrEmpty(redirectUri) || !Url.IsLocalUrl(redirectUri))
        {
            return LocalRedirect("~/");
        }

        return LocalRedirect(redirectUri);
    }

    [HttpGet]
    public async Task<IActionResult> SignOutAction()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        logger.LogInformation("SignOut initiated for user {UserId}", userId.SanitizeForLogging());
        
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect("~/");
    }
}
