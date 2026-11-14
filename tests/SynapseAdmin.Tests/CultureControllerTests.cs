using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using SynapseAdmin.Controllers;
using SynapseAdmin.Infrastructure.Helpers;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>Issue #394: the culture cookie endpoint only persists supported cultures.</summary>
public class CultureControllerTests
{
    private static (CultureController Controller, HttpContext Context) CreateController()
    {
        var context = new DefaultHttpContext();
        var controller = new CultureController
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        controller.Url = new UrlHelper(new ActionContext(context, new RouteData(), new ActionDescriptor()));
        return (controller, context);
    }

    private static string? CultureCookie(HttpContext context) =>
        context.Response.Headers.SetCookie
            .FirstOrDefault(c => c != null && c.StartsWith(CookieRequestCultureProvider.DefaultCookieName + "=", StringComparison.Ordinal));

    [Theory]
    [InlineData("de-DE", "de-DE")]
    [InlineData("fr-FR", "fr-FR")]
    [InlineData(" de-de ", "de-DE")]
    public void Set_SupportedCulture_PersistsCanonicalName(string culture, string expected)
    {
        var (controller, context) = CreateController();

        var result = controller.Set(culture, "/rooms");

        var cookie = CultureCookie(context);
        Assert.NotNull(cookie);
        var value = CookieRequestCultureProvider.ParseCookieValue(Uri.UnescapeDataString(cookie.Split(';')[0].Split('=', 2)[1]));
        Assert.Equal(expected, value!.Cultures.Single().Value);
        Assert.Equal(expected, value.UICultures.Single().Value);
        Assert.Equal("/rooms", Assert.IsType<LocalRedirectResult>(result).Url);
    }

    [Theory]
    [InlineData("xx-INVALID!!")] // malformed: RequestCulture would throw
    [InlineData("ja-JP")]        // valid but unsupported
    [InlineData("")]
    [InlineData(null)]
    public void Set_UnsupportedOrMalformedCulture_DoesNotSetCookie_AndStillRedirects(string? culture)
    {
        var (controller, context) = CreateController();

        var result = controller.Set(culture!, "/users");

        Assert.Null(CultureCookie(context));
        Assert.Equal("/users", Assert.IsType<LocalRedirectResult>(result).Url);
    }

    [Fact]
    public void Set_NonLocalRedirect_FallsBackToRoot()
    {
        var (controller, _) = CreateController();

        var result = controller.Set(SupportedCultures.German, "https://evil.example/");

        Assert.Equal("~/", Assert.IsType<LocalRedirectResult>(result).Url);
    }

    [Fact]
    public void SupportedCultures_DefaultIsSupported() =>
        Assert.Contains(SupportedCultures.Default, SupportedCultures.All);
}
