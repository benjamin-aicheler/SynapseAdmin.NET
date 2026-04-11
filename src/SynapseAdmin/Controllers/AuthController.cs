using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Controllers;

[Route("[controller]/[action]")]
public class AuthController(ISessionBridgeService bridgeService) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SignIn(string key, string? redirectUri = null)
    {
        if (!bridgeService.TryConsumeBridge(key, out var data))
        {
            return Unauthorized("Invalid or expired login key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, data.UserId),
            new("Homeserver", data.Homeserver),
            new("AccessToken", data.AccessToken)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
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
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect("~/");
    }
}
