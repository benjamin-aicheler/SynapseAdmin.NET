using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class ShinjiTheme : IAppTheme
{
    public string Id => "shinji";
    public string Name => "Shinji (Unit-01)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#5E2B97",
            Secondary = "#2E8B57",
            AppbarBackground = "#5E2B97",
            Background = "#F8F9FA",
            Surface = "#F0F0F0",
            TextPrimary = "#2E3440"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#00FF66",
            Secondary = "#9D4EDD",
            AppbarBackground = "#160B24",
            Background = "#12091F",
            Surface = "#1E1032",
            TextPrimary = "#FFFFFF",
            TextSecondary = "#9D4EDD",
            ActionDefault = "#00FF66",
            DrawerBackground = "#1A0F2B"
        }
    };
}
