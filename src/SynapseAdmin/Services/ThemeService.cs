using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using MudBlazor;
using SynapseAdmin.Infrastructure.Themes;
using SynapseAdmin.Interfaces;
using System.Reflection;
using SynapseAdmin.Extensions;

namespace SynapseAdmin.Services;

public class ThemeService(ProtectedLocalStorage localStorage, ILogger<ThemeService> logger) : IThemeService
{
    private const string StorageKey_ThemeId = "app_theme_id";
    private const string StorageKey_DarkMode = "app_dark_mode";

    // Discovered once per process, logging through the first instance's logger.
    private static IReadOnlyList<IAppTheme>? _cachedThemes;
    private static object? _discoveryLock;

    private IAppTheme _currentThemeInstance = new DefaultTheme();
    private bool _isDarkMode = true;
    private bool _isInitialized;

    public MudTheme CurrentTheme => _currentThemeInstance.Theme;
    public string CurrentThemeId => _currentThemeInstance.Id;

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                logger.LogDebug("Dark mode toggled to: {IsDarkMode}", value);
                _isDarkMode = value;
                OnThemeChanged?.Invoke();
            }
        }
    }

    public IReadOnlyList<IAppTheme> AvailableThemes =>
        LazyInitializer.EnsureInitialized(ref _cachedThemes, ref _discoveryLock,
            () => DiscoverThemes(Assembly.GetExecutingAssembly().GetTypes(), logger));

    public event Action? OnThemeChanged;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            logger.LogDebug("Initializing ThemeService from local storage...");
            var themeResult = await localStorage.GetAsync<string>(StorageKey_ThemeId);
            if (themeResult.Success && !string.IsNullOrEmpty(themeResult.Value))
            {
                var theme = AvailableThemes.FirstOrDefault(t => t.Id == themeResult.Value);
                if (theme != null)
                {
                    logger.LogInformation("Restored theme: {ThemeId}", theme.Id.SanitizeForLogging());
                    _currentThemeInstance = theme;
                }
            }

            var darkModeResult = await localStorage.GetAsync<bool>(StorageKey_DarkMode);
            if (darkModeResult.Success)
            {
                logger.LogInformation("Restored Dark Mode preference: {IsDarkMode}", darkModeResult.Value);
                _isDarkMode = darkModeResult.Value;
            }

            _isInitialized = true;
            OnThemeChanged?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize ThemeService from local storage.");
            // Fail silently, defaults are already set
        }
    }

    public async Task SetThemeAsync(string themeId)
    {
        var theme = AvailableThemes.FirstOrDefault(t => t.Id == themeId);
        if (theme != null && theme.Id != _currentThemeInstance.Id)
        {
            logger.LogInformation("Switching theme to: {ThemeId} ({ThemeName})", theme.Id.SanitizeForLogging(), theme.Name.SanitizeForLogging());
            _currentThemeInstance = theme;
            try
            {
                await localStorage.SetAsync(StorageKey_ThemeId, themeId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist theme preference to local storage.");
            }
            OnThemeChanged?.Invoke();
        }
    }

    public async Task ToggleDarkModeAsync(bool isDarkMode)
    {
        if (IsDarkMode != isDarkMode)
        {
            IsDarkMode = isDarkMode;
            try
            {
                await localStorage.SetAsync(StorageKey_DarkMode, IsDarkMode);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist dark mode preference to local storage.");
            }
        }
    }

    /// <summary>
    /// Creates one instance of every concrete <see cref="IAppTheme"/> in <paramref name="types"/>, the default theme
    /// first and the others by name. A theme that can't be created is skipped with a warning.
    /// </summary>
    internal static IReadOnlyList<IAppTheme> DiscoverThemes(IEnumerable<Type> types, ILogger logger)
    {
        var themeTypes = types
            .Where(t => typeof(IAppTheme).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        var themes = new List<IAppTheme>();
        foreach (var type in themeTypes)
        {
            try
            {
                if (Activator.CreateInstance(type) is IAppTheme theme)
                {
                    themes.Add(theme);
                }
            }
            catch (Exception ex)
            {
                // For example a theme without a parameterless constructor, or one whose constructor throws.
                logger.LogWarning(ex, "Theme {ThemeType} could not be created and is not offered", type.FullName);
            }
        }

        // Ensure default theme is always there and potentially first
        if (!themes.Any(t => t.Id == "default"))
        {
            themes.Insert(0, new DefaultTheme());
        }

        return themes.OrderBy(t => t.Id == "default" ? 0 : 1).ThenBy(t => t.Name).ToList();
    }
}
