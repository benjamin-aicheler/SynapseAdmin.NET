using System.Reflection;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Infrastructure.Themes;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
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

    // O-01: a theme that can't be created is skipped, as before, but no longer silently.
    [Fact]
    public void DiscoverThemes_SkipsAThemeThatCannotBeCreated_WithAWarning()
    {
        var logger = new RecordingLogger<ThemeService>();

        var themes = ThemeService.DiscoverThemes([typeof(WorkingTheme), typeof(ThrowingTheme), typeof(NoDefaultConstructorTheme), typeof(string)], logger);

        Assert.Equal(["default", "working"], themes.Select(t => t.Id));
        Assert.Collection(logger.Entries,
            e => AssertSkipped(e, typeof(ThrowingTheme), typeof(TargetInvocationException)),
            e => AssertSkipped(e, typeof(NoDefaultConstructorTheme), typeof(MissingMethodException)));
    }

    [Fact]
    public void DiscoverThemes_WithoutFailures_LogsNothing()
    {
        var logger = new RecordingLogger<ThemeService>();

        ThemeService.DiscoverThemes(typeof(IAppTheme).Assembly.GetTypes(), logger);

        Assert.Empty(logger.Entries);
    }

    private static void AssertSkipped(RecordedLog entry, Type theme, Type exception)
    {
        Assert.Equal((LogLevel.Warning, "Theme {ThemeType} could not be created and is not offered"), (entry.Level, entry.Template));
        Assert.Equal(new Dictionary<string, object?> { ["ThemeType"] = theme.FullName }, entry.Arguments);
        Assert.IsType(exception, entry.Exception);
    }

    private sealed class WorkingTheme : IAppTheme
    {
        public string Id => "working";
        public string Name => "Working";
        public MudTheme Theme { get; } = new();
    }

    private sealed class ThrowingTheme : IAppTheme
    {
        public ThrowingTheme() => throw new InvalidOperationException("broken theme");
        public string Id => "throwing";
        public string Name => "Throwing";
        public MudTheme Theme { get; } = new();
    }

    private sealed class NoDefaultConstructorTheme(string id) : IAppTheme
    {
        public string Id => id;
        public string Name => "No default constructor";
        public MudTheme Theme { get; } = new();
    }

    private static ThemeService CreateService() =>
        new(new ProtectedLocalStorage(Stub.Create<IJSRuntime>(), new EphemeralDataProtectionProvider()), NullLogger<ThemeService>.Instance);
}
