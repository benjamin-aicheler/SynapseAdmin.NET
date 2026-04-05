using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class CyberpunkTheme : IAppTheme
{
    public string Id => "cyberpunk";
    public string Name => "Cyberpunk (Synthwave)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#B500FF",
            Secondary = "#00D1FF",
            AppbarBackground = "#B500FF",
            Background = "#FAFAFA",
            Surface = "#F0F0F0"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#FF00FF",
            Secondary = "#00FFFF",
            AppbarBackground = "#1A1A2E",
            Background = "#0F0C29",
            Surface = "#1A1A2E",
            TextPrimary = "#00FFFF",
            TextSecondary = "#FF00FF",
            ActionDefault = "#00FFFF",
            DrawerBackground = "#16213E"
        }
    };
}
