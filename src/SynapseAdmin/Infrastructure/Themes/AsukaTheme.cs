using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class AsukaTheme : IAppTheme
{
    public string Id => "asuka";
    public string Name => "Asuka (Unit-02)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#C1272D",
            Secondary = "#F58220",
            AppbarBackground = "#C1272D",
            Background = "#FAF9F6",
            Surface = "#F0EFEA",
            TextPrimary = "#2E3440"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#FF3B30",
            Secondary = "#FF5722",
            AppbarBackground = "#200E0E",
            Background = "#140C0C",
            Surface = "#1E1313",
            TextPrimary = "#FFF3E0",
            TextSecondary = "#FF5722",
            ActionDefault = "#FF3B30",
            DrawerBackground = "#2B1A1A"
        }
    };
}
