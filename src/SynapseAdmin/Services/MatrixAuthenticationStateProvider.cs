using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using SynapseAdmin.Models;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Extensions;

namespace SynapseAdmin.Services;

public class MatrixAuthenticationStateProvider(
    ProtectedLocalStorage localStorage,
    IMatrixSessionService sessionService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<MatrixAuthenticationStateProvider> logger) : AuthenticationStateProvider
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
                
                if (restoreResult.Success && sessionService.IsLoggedIn && sessionService.Gateway != null)
                {
                    // PROACTIVE SYNC: Check if the cookie exists. 
                    // Standard controllers need the cookie to work.
                    var httpContext = httpContextAccessor.HttpContext;
                    var isCookieAuthenticated = httpContext?.User?.Identity?.IsAuthenticated ?? false;
                    
                    if (!isCookieAuthenticated)
                    {
                        logger.LogInformation("Blazor session restored for user {UserId}, but authentication cookie is missing. Synchronization will be handled by UI components.", sessionService.Gateway.UserId.SanitizeForLogging());
                    }

                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, sessionService.Gateway.UserId),
                        new Claim(ClaimTypes.Name, sessionService.Gateway.Username),
                        new Claim("Homeserver", sessionService.Gateway.ServerName),
                        new Claim("AccessToken", sessionService.Gateway.AccessToken)
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
            logger.LogDebug(ex, "Failed to read from local storage during prerendering.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during GetAuthenticationStateAsync");
        }

        _cachedState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        return _cachedState;
    }

    public async Task<OperationResult> LoginAsync(string homeserver, string username, string password)
    {
        var result = await sessionService.LoginAsync(homeserver, username, password);
        
        if (result.Success && sessionService.IsLoggedIn && sessionService.Gateway != null)
        {
            await localStorage.SetAsync(StorageKey_Homeserver, homeserver);
            await localStorage.SetAsync(StorageKey_AccessToken, sessionService.Gateway.AccessToken);
            
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sessionService.Gateway.UserId),
                new Claim(ClaimTypes.Name, sessionService.Gateway.Username),
                new Claim("Homeserver", sessionService.Gateway.ServerName),
                new Claim("AccessToken", sessionService.Gateway.AccessToken)
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

        if (result.Success && sessionService.IsLoggedIn && sessionService.Gateway != null)
        {
            await localStorage.SetAsync(StorageKey_Homeserver, homeserver);
            await localStorage.SetAsync(StorageKey_AccessToken, accessToken);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sessionService.Gateway.UserId),
                new Claim(ClaimTypes.Name, sessionService.Gateway.Username),
                new Claim("Homeserver", sessionService.Gateway.ServerName),
                new Claim("AccessToken", sessionService.Gateway.AccessToken)
            };

            var identity = new ClaimsIdentity(claims, "MatrixAuth");
            var principal = new ClaimsPrincipal(identity);

            _cachedState = new AuthenticationState(principal);
            NotifyAuthenticationStateChanged(Task.FromResult(_cachedState));
            return result;
        }

        return result;
    }

    public string? GetAccessToken() => sessionService.Gateway?.AccessToken;
    public string? GetUserId() => sessionService.Gateway?.UserId;
    public string? GetUsername() => sessionService.Gateway?.Username;
    public string? GetHomeserver() => sessionService.Gateway?.HomeserverUrl;

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
