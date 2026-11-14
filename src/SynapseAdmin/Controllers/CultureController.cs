using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using SynapseAdmin.Infrastructure.Helpers;

namespace SynapseAdmin.Controllers;

[Route("[controller]/[action]")]
public class CultureController : Controller
{
    public IActionResult Set(string culture, string redirectUri)
    {
        // Only persist cultures the app supports: anything else would be ignored by request localization
        // anyway, and malformed names would make RequestCulture throw (#394).
        var supportedCulture = SupportedCultures.Normalize(culture);
        if (supportedCulture != null)
        {
            HttpContext.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(supportedCulture, supportedCulture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    Secure = true
                });
        }

        if (string.IsNullOrEmpty(redirectUri) || !Url.IsLocalUrl(redirectUri))
        {
            return LocalRedirect("~/");
        }

        return LocalRedirect(redirectUri);
    }
}
