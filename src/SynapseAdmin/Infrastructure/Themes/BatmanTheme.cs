using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class BatmanTheme : IAppTheme
{
    public string Id => "batman";
    public string Name => "Batman (Dark Knight)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#1C1C1C",
            Secondary = "#FFD700",
            AppbarBackground = "#1C1C1C",
            Background = "#F5F5F5",
            Surface = "#E0E0E0",
            TextPrimary = "#121212"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#FFEA00",
            Secondary = "#757575",
            AppbarBackground = "#121212",
            Background = "#000000",
            Surface = "#1C1C1C",
            TextPrimary = "#E0E0E0",
            TextSecondary = "#9E9E9E",
            ActionDefault = "#FFEA00",
            DrawerBackground = "#151515"
        }
    };
}
