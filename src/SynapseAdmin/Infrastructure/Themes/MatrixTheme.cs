using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class MatrixTheme : IAppTheme
{
    public string Id => "matrix";
    public string Name => "Matrix (Green)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#00bb00",
            AppbarBackground = "#00bb00",
            AppbarText = "#ffffff",
            PrimaryDarken = "#008800",
            PrimaryLighten = "#00ee00"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#00ff00",
            AppbarBackground = "#000000",
            Background = "#0a0a0a",
            Surface = "#1a1a1a",
            TextPrimary = "#00ff00",
            TextSecondary = "#00cc00"
        }
    };
}
