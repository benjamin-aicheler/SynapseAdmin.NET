using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class MariTheme : IAppTheme
{
    public string Id => "mari";
    public string Name => "Mari (Unit-08)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#D24D57",
            Secondary = "#5F8575",
            AppbarBackground = "#D24D57",
            Background = "#FFF0F5",
            Surface = "#F5E6EB",
            TextPrimary = "#2E3440"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#FF1493",
            Secondary = "#8A9A5B",
            AppbarBackground = "#230F20",
            Background = "#1B0B19",
            Surface = "#2E142B",
            TextPrimary = "#FFB7C5",
            TextSecondary = "#8A9A5B",
            ActionDefault = "#FF1493",
            DrawerBackground = "#2B1527"
        }
    };
}
