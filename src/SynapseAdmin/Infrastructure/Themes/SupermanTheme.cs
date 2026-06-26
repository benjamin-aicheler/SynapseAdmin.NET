using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class SupermanTheme : IAppTheme
{
    public string Id => "superman";
    public string Name => "Superman (Metropolis)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#1565C0",
            Secondary = "#C62828",
            AppbarBackground = "#1565C0",
            Background = "#F0F4F8",
            Surface = "#E3F2FD",
            TextPrimary = "#1A237E"
        },
        PaletteDark = new PaletteDark()
        {
            Primary = "#1E88E5",
            Secondary = "#E53935",
            AppbarBackground = "#0D1B2A",
            Background = "#0B132B",
            Surface = "#1C2541",
            TextPrimary = "#FFFFFF",
            TextSecondary = "#E2E8F0",
            ActionDefault = "#1E88E5",
            DrawerBackground = "#0E1E38"
        }
    };
}
