using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IDisposable
{
    [Inject]
    public IThemeService ThemeService { get; set; } = default!;

    private bool _drawerOpen = true;
    private bool _isRtl => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    protected override void OnInitialized()
    {
        ThemeService.OnThemeChanged += StateHasChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await ThemeService.InitializeAsync();
        }
    }

    private void DrawerToggle()
    {
        _drawerOpen = !_drawerOpen;
    }

    public void Dispose()
    {
        ThemeService.OnThemeChanged -= StateHasChanged;
    }
}
