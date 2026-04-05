using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public class DefaultTheme : IAppTheme
{
    public string Id => "default";
    public string Name => "Indigo (Default)";

    public MudTheme Theme => new MudTheme
    {
        PaletteLight = new PaletteLight()
        {
            Primary = Colors.Indigo.Default,
            AppbarBackground = Colors.Indigo.Default,
        },
        PaletteDark = new PaletteDark()
        {
            Primary = Colors.Indigo.Lighten1,
        }
    };
}
