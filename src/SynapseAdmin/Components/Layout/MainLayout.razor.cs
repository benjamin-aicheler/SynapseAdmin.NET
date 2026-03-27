using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace SynapseAdmin.Components.Layout;

public partial class MainLayout : LayoutComponentBase
{
    private bool _drawerOpen = true;
    private bool _isDarkMode = true;
    private bool _isRtl => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    private MudTheme _theme = new MudTheme
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

    private void DrawerToggle()
    {
        _drawerOpen = !_drawerOpen;
    }
}