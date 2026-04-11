using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Layout;

public partial class NavMenu : ComponentBase
{
    [Inject] private Services.MatrixAuthenticationStateProvider AuthProvider { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;
    [Inject] private ISessionBridgeService BridgeService { get; set; } = default!;

    protected override void OnInitialized()
    {
        CheckSessionSync();
    }

    private void CheckSessionSync()
    {
        // Don't sync if we are already on the login or auth pages
        var currentUri = Navigation.Uri.ToLower();
        if (currentUri.Contains("/login") || currentUri.Contains("/auth/")) return;

        // If Blazor is logged in but the Cookie is missing, we need to bridge.
        var isBlazorAuth = AuthProvider.GetUserId() != null;
        var isCookieAuth = HttpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

        if (isBlazorAuth && !isCookieAuth)
        {
            var homeserver = AuthProvider.GetHomeserver();
            var token = AuthProvider.GetAccessToken();
            var userId = AuthProvider.GetUserId();

            if (homeserver != null && token != null && userId != null)
            {
                // Use the bridge to hide sensitive data from the URL during proactive sync
                var key = BridgeService.CreateBridge(homeserver, token, userId);
                var url = $"/Auth/SignIn?key={Uri.EscapeDataString(key)}&redirectUri={Uri.EscapeDataString(Navigation.Uri)}";
                Navigation.NavigateTo(url, forceLoad: true);
            }
        }
    }

    private async Task Logout()
    {
        await AuthProvider.LogoutAsync(Navigation);
    }
}
