using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Infrastructure.Themes;
using SynapseAdmin.Interfaces;
using System.Reflection;

namespace SynapseAdmin.Services;

public class ThemeService(ProtectedLocalStorage localStorage, ILogger<ThemeService> logger) : IThemeService
{
    private const string StorageKey_ThemeId = "app_theme_id";
    private const string StorageKey_DarkMode = "app_dark_mode";

    private List<IAppTheme>? _availableThemes;
    private IAppTheme _currentThemeInstance = new DefaultTheme();
    private bool _isDarkMode = true;
    private bool _isInitialized = false;

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

    public IReadOnlyList<IAppTheme> AvailableThemes
    {
        get
        {
            if (_availableThemes == null)
            {
                _availableThemes = DiscoverThemes();
            }
            return _availableThemes;
        }
    }

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
                    logger.LogInformation("Restored theme: {ThemeId}", theme.Id);
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
            logger.LogInformation("Switching theme to: {ThemeId} ({ThemeName})", theme.Id, theme.Name);
            _currentThemeInstance = theme;
            await localStorage.SetAsync(StorageKey_ThemeId, themeId);
            OnThemeChanged?.Invoke();
        }
    }

    public async Task ToggleDarkModeAsync(bool isDarkMode)
    {
        if (IsDarkMode != isDarkMode)
        {
            IsDarkMode = isDarkMode;
            await localStorage.SetAsync(StorageKey_DarkMode, IsDarkMode);
        }
    }

    private List<IAppTheme> DiscoverThemes()
    {
        logger.LogDebug("Discovering themes via reflection...");
        var themeTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IAppTheme).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        var themes = new List<IAppTheme>();
        foreach (var type in themeTypes)
        {
            try
            {
                if (Activator.CreateInstance(type) is IAppTheme theme)
                {
                    logger.LogDebug("Found theme: {ThemeId} ({ThemeName})", theme.Id, theme.Name);
                    themes.Add(theme);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to instantiate theme type: {TypeName}", type.FullName);
            }
        }

        // Ensure default theme is always there and potentially first
        if (!themes.Any(t => t.Id == "default"))
        {
            logger.LogDebug("Adding DefaultTheme to discovered themes.");
            themes.Insert(0, new DefaultTheme());
        }

        var result = themes.OrderBy(t => t.Id == "default" ? 0 : 1).ThenBy(t => t.Name).ToList();
        logger.LogInformation("Discovered {Count} themes.", result.Count);
        return result;
    }
}
