using Microsoft.AspNetCore.Components;

namespace SynapseAdmin.Components.Layout;

public partial class NavMenu : ComponentBase
{
    [Inject] private Services.MatrixAuthenticationStateProvider AuthProvider { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private async Task Logout()
    {
        await AuthProvider.LogoutAsync();
        Navigation.NavigateTo("/login");
    }
}