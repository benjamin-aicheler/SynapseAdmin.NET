using Microsoft.AspNetCore.Components;

namespace SynapseAdmin.Components;

public partial class RedirectToLogin : ComponentBase
{
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    protected override void OnInitialized()
    {
        NavigationManager.NavigateTo("login", forceLoad: true);
    }
}