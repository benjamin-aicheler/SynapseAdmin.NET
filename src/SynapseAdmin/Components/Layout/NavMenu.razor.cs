using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Layout;

public partial class NavMenu : ComponentBase
{
    [Inject] private Services.MatrixAuthenticationStateProvider AuthProvider { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;
    [Inject] private ISessionBridgeService BridgeService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ILogger<NavMenu> Logger { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await CheckSessionSync();
    }

    private async Task CheckSessionSync()
    {
        // Don't sync if we are already on the login or auth pages
        var currentUri = Navigation.Uri.ToLower();
        if (currentUri.Contains("/login") || currentUri.Contains("/auth/")) return;

        // Ensure the AuthProvider has had a chance to restore the session from LocalStorage
        await AuthProvider.GetAuthenticationStateAsync();

        // If Blazor is logged in but the Cookie is missing, we need to bridge.
        var isBlazorAuth = AuthProvider.GetUserId() != null;
        var isCookieAuth = HttpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

        if (isBlazorAuth && !isCookieAuth)
        {
            if (!CanStoreSecureCookie())
            {
                Logger.LogWarning(
                    "Skipping authentication cookie synchronization because the current origin ({Uri}) is neither HTTPS nor localhost. Media streaming requires HTTPS or a reverse proxy setting X-Forwarded-Proto: https.",
                    Navigation.Uri.SanitizeForLogging());
                Snackbar.Add(L["HttpsRequiredForMediaWarning"], Severity.Warning);
                return;
            }

            var homeserver = AuthProvider.GetHomeserver();
            var token = AuthProvider.GetAccessToken();
            var userId = AuthProvider.GetUserId();
            var username = AuthProvider.GetUsername();

            if (homeserver != null && token != null && userId != null && username != null)
            {
                // Use the bridge to hide sensitive data from the URL during proactive sync
                var key = BridgeService.CreateBridge(homeserver, token, userId, username);
                var url = $"/Auth/SignIn?key={Uri.EscapeDataString(key)}&redirectUri={Uri.EscapeDataString(Navigation.Uri)}";
                Navigation.NavigateTo(url, forceLoad: true);
            }
        }
    }

    private bool CanStoreSecureCookie()
    {
        if (HttpContextAccessor.HttpContext?.Request.IsHttps == true)
        {
            return true;
        }

        if (Uri.TryCreate(Navigation.Uri, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return uri.IsLoopback;
        }

        return false;
    }

    private async Task Logout()
    {
        await AuthProvider.LogoutAsync(Navigation);
    }
}
