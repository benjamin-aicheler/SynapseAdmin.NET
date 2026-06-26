using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class ReiTheme : IAppTheme
{
    public string Id => "rei";
    public string Name => "Rei (Unit-00)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#0077B6",
            Secondary = "#FDA085",
            AppbarBackground = "#0077B6",
            Background = "#F0F4F8",
            Surface = "#E1E6EB",
            TextPrimary = "#2E3440"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#90E0EF",
            Secondary = "#FFB703",
            AppbarBackground = "#111A2E",
            Background = "#0B132B",
            Surface = "#1B263B",
            TextPrimary = "#E0F2F1",
            TextSecondary = "#FFB703",
            ActionDefault = "#90E0EF",
            DrawerBackground = "#1C2541"
        }
    };
}
