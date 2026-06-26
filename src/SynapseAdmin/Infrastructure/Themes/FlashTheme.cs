using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class FlashTheme : IAppTheme
{
    public string Id => "flash";
    public string Name => "The Flash (Speed Force)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#E53935",
            Secondary = "#FFEB3B",
            AppbarBackground = "#E53935",
            Background = "#FFFDE7",
            Surface = "#FFF9C4",
            TextPrimary = "#212121"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#FF1744",
            Secondary = "#FFEA00",
            AppbarBackground = "#1A0004",
            Background = "#121212",
            Surface = "#1E1215",
            TextPrimary = "#FFFDE7",
            TextSecondary = "#FFEA00",
            ActionDefault = "#FF1744",
            DrawerBackground = "#1A0A0D"
        }
    };
}
