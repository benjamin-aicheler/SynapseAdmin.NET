using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class NordTheme : IAppTheme
{
    public string Id => "nord";
    public string Name => "Nord (Arctic)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#5E81AC",
            Secondary = "#81A1C1",
            AppbarBackground = "#81A1C1",
            Background = "#ECEFF4",
            Surface = "#E5E9F0",
            TextPrimary = "#2E3440",
            TextSecondary = "#4C566A"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#88C0D0",
            Secondary = "#81A1C1",
            AppbarBackground = "#3B4252",
            Background = "#2E3440",
            Surface = "#3B4252",
            TextPrimary = "#D8DEE9",
            TextSecondary = "#E5E9F0"
        }
    };
}
