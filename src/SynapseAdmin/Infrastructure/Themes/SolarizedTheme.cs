using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class SolarizedTheme : IAppTheme
{
    public string Id => "solarized";
    public string Name => "Solarized (Professional)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#268BD2",
            Secondary = "#859900",
            AppbarBackground = "#FDF6E3",
            AppbarText = "#586E75",
            Background = "#FDF6E3",
            Surface = "#EEE8D5",
            TextPrimary = "#586E75",
            TextSecondary = "#657B83"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#268BD2",
            Secondary = "#859900",
            AppbarBackground = "#002B36",
            Background = "#002B36",
            Surface = "#073642",
            TextPrimary = "#839496",
            TextSecondary = "#93A1A1",
            AppbarText = "#839496"
        }
    };
}
