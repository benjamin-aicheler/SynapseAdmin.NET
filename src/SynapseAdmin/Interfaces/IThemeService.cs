using MudBlazor;
using SynapseAdmin.Infrastructure.Themes;

namespace SynapseAdmin.Interfaces;

public interface IThemeService
{
    MudTheme CurrentTheme { get; }
    string CurrentThemeId { get; }
    bool IsDarkMode { get; set; }
    IReadOnlyList<IAppTheme> AvailableThemes { get; }
    Task SetThemeAsync(string themeId);
    Task ToggleDarkModeAsync(bool isDarkMode);
    Task InitializeAsync();
    event Action? OnThemeChanged;
}
