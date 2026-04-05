using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using MudBlazor;
using SynapseAdmin.Infrastructure.Themes;
using SynapseAdmin.Interfaces;
using System.Reflection;

namespace SynapseAdmin.Services;

public class ThemeService(ProtectedLocalStorage localStorage) : IThemeService
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
            var themeResult = await localStorage.GetAsync<string>(StorageKey_ThemeId);
            if (themeResult.Success && !string.IsNullOrEmpty(themeResult.Value))
            {
                var theme = AvailableThemes.FirstOrDefault(t => t.Id == themeResult.Value);
                if (theme != null)
                {
                    _currentThemeInstance = theme;
                }
            }

            var darkModeResult = await localStorage.GetAsync<bool>(StorageKey_DarkMode);
            if (darkModeResult.Success)
            {
                _isDarkMode = darkModeResult.Value;
            }

            _isInitialized = true;
            OnThemeChanged?.Invoke();
        }
        catch
        {
            // Fail silently, defaults are already set
        }
    }

    public async Task SetThemeAsync(string themeId)
    {
        var theme = AvailableThemes.FirstOrDefault(t => t.Id == themeId);
        if (theme != null && theme.Id != _currentThemeInstance.Id)
        {
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
                    themes.Add(theme);
                }
            }
            catch
            {
                // Log error or skip invalid theme
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
