using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Http;
using SynapseAdmin.Models;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Services;

public class MatrixAuthenticationStateProvider(
    ProtectedLocalStorage localStorage,
    IMatrixSessionService sessionService,
    IHttpContextAccessor httpContextAccessor) : AuthenticationStateProvider
{
    private const string StorageKey_Homeserver = "matrix_homeserver";
    private const string StorageKey_AccessToken = "matrix_access_token";

    private AuthenticationState? _cachedState;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cachedState != null) return _cachedState;

        try
        {
            // Attempt to read from local storage
            var homeserverResult = await localStorage.GetAsync<string>(StorageKey_Homeserver);
            var tokenResult = await localStorage.GetAsync<string>(StorageKey_AccessToken);

            if (homeserverResult.Success && !string.IsNullOrEmpty(homeserverResult.Value) &&
                tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Value))
            {
                // We have a stored session, restore it
                var restoreResult = await sessionService.RestoreSessionAsync(homeserverResult.Value, tokenResult.Value);
                
                if (restoreResult.Success && sessionService.IsLoggedIn)
                {
                    // PROACTIVE SYNC: Check if the cookie exists. 
                    // Standard controllers need the cookie to work.
                    var httpContext = httpContextAccessor.HttpContext;
                    var isCookieAuthenticated = httpContext?.User?.Identity?.IsAuthenticated ?? false;
                    
                    if (!isCookieAuthenticated)
                    {
                        // The Blazor session exists but the Cookie is gone!
                        // We can't set cookies from here, so we return the state 
                        // but the app should handle the redirect if needed or the user 
                        // will be synced on next Login. 
                        // Note: NavigationManager is often not available yet in the first call of this method.
                    }

                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, sessionService.AuthenticatedHomeserver!.UserId),
                        new Claim(ClaimTypes.Name, sessionService.AuthenticatedHomeserver!.UserLocalpart),
                        new Claim("Homeserver", sessionService.AuthenticatedHomeserver!.ServerName),
                        new Claim("AccessToken", sessionService.AuthenticatedHomeserver!.AccessToken)
                    };

                    var identity = new ClaimsIdentity(claims, "MatrixAuth");
                    var principal = new ClaimsPrincipal(identity);
                    _cachedState = new AuthenticationState(principal);
                    return _cachedState;
                }
                else
                {
                    // If session restoration failed, clean up storage
                    await localStorage.DeleteAsync(StorageKey_Homeserver);
                    await localStorage.DeleteAsync(StorageKey_AccessToken);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.JSInterop.JSException)
        {
            // ProtectedLocalStorage can throw if JS is not available (e.g. prerendering)
            // Or if data is tampered with. We just fallback to unauthenticated.
        }
        catch
        {
            // Unexpected errors should be logged or handled more explicitly if needed
            throw;
        }

        _cachedState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        return _cachedState;
    }

    public async Task<OperationResult> LoginAsync(string homeserver, string username, string password)
    {
        var result = await sessionService.LoginAsync(homeserver, username, password);
        
        if (result.Success && sessionService.IsLoggedIn)
        {
            await localStorage.SetAsync(StorageKey_Homeserver, homeserver);
            await localStorage.SetAsync(StorageKey_AccessToken, sessionService.AuthenticatedHomeserver!.AccessToken);
            
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sessionService.AuthenticatedHomeserver!.UserId),
                new Claim(ClaimTypes.Name, sessionService.AuthenticatedHomeserver!.UserLocalpart),
                new Claim("Homeserver", sessionService.AuthenticatedHomeserver!.ServerName)
            };

            var identity = new ClaimsIdentity(claims, "MatrixAuth");
            var principal = new ClaimsPrincipal(identity);
            
            _cachedState = new AuthenticationState(principal);
            NotifyAuthenticationStateChanged(Task.FromResult(_cachedState));
            return result;
        }
        
        return result;
    }

    public async Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken)
    {
        var result = await sessionService.LoginWithTokenAsync(homeserver, accessToken);

        if (result.Success && sessionService.IsLoggedIn)
        {
            await localStorage.SetAsync(StorageKey_Homeserver, homeserver);
            await localStorage.SetAsync(StorageKey_AccessToken, accessToken);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sessionService.AuthenticatedHomeserver!.UserId),
                new Claim(ClaimTypes.Name, sessionService.AuthenticatedHomeserver!.UserLocalpart),
                new Claim("Homeserver", sessionService.AuthenticatedHomeserver!.ServerName)
            };

            var identity = new ClaimsIdentity(claims, "MatrixAuth");
            var principal = new ClaimsPrincipal(identity);

            _cachedState = new AuthenticationState(principal);
            NotifyAuthenticationStateChanged(Task.FromResult(_cachedState));
            return result;
        }

        return result;
    }

    public string? GetAccessToken() => sessionService.AuthenticatedHomeserver?.AccessToken;
    public string? GetUserId() => sessionService.AuthenticatedHomeserver?.UserId;
    public string? GetHomeserver() => sessionService.AuthenticatedHomeserver?.BaseUrl;

    public async Task LogoutAsync(NavigationManager? navigation = null)
    {
        sessionService.Logout();
        await localStorage.DeleteAsync(StorageKey_Homeserver);
        await localStorage.DeleteAsync(StorageKey_AccessToken);
        
        _cachedState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        NotifyAuthenticationStateChanged(Task.FromResult(_cachedState));

        if (navigation != null)
        {
            navigation.NavigateTo("/Auth/SignOutAction", forceLoad: true);
        }
    }
}
