using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SynapseAdmin.Infrastructure.Themes;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Theme discovery (<see cref="ThemeService.AvailableThemes"/>): every <see cref="IAppTheme"/> in the app assembly is
/// offered once, the default theme first and the others by name.
/// </summary>
public sealed class ThemeDiscoveryTests
{
    [Fact]
    public void AvailableThemes_OfferEveryThemeOnce_DefaultFirst_ThenByName()
    {
        var themeTypes = typeof(IAppTheme).Assembly.GetTypes()
            .Where(t => typeof(IAppTheme).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        var themes = CreateService().AvailableThemes;

        Assert.Equal(themeTypes.Count, themes.Count);
        Assert.Equal(themeTypes.OrderBy(t => t.FullName), themes.Select(t => t.GetType()).OrderBy(t => t.FullName));
        Assert.Equal(themes.Count, themes.Select(t => t.Id).Distinct().Count());
        Assert.Equal("default", themes[0].Id);
        Assert.Equal(themes.Skip(1).Select(t => t.Name).Order(), themes.Skip(1).Select(t => t.Name));
    }

    private static ThemeService CreateService() =>
        new(new ProtectedLocalStorage(Stub.Create<IJSRuntime>(), new EphemeralDataProtectionProvider()), NullLogger<ThemeService>.Instance);
}
