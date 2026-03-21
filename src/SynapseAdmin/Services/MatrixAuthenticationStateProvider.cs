using System.Security.Claims;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace SynapseAdmin.Services;

public class MatrixAuthenticationStateProvider(
    ProtectedLocalStorage localStorage,
    MatrixSessionService sessionService) : AuthenticationStateProvider
{
    private const string StorageKey_Homeserver = "matrix_homeserver";
    private const string StorageKey_AccessToken = "matrix_access_token";

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            // Attempt to read from local storage
            var homeserverResult = await localStorage.GetAsync<string>(StorageKey_Homeserver);
            var tokenResult = await localStorage.GetAsync<string>(StorageKey_AccessToken);

            if (homeserverResult.Success && !string.IsNullOrEmpty(homeserverResult.Value) &&
                tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Value))
            {
                // We have a stored session, restore it
                await sessionService.RestoreSessionAsync(homeserverResult.Value, tokenResult.Value);
                
                if (sessionService.IsLoggedIn)
                {
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, sessionService.AuthenticatedHomeserver!.UserId),
                        new Claim(ClaimTypes.Name, sessionService.AuthenticatedHomeserver!.UserLocalpart),
                        new Claim("Homeserver", sessionService.AuthenticatedHomeserver!.ServerName)
                    };

                    var identity = new ClaimsIdentity(claims, "MatrixAuth");
                    var principal = new ClaimsPrincipal(identity);
                    return new AuthenticationState(principal);
                }
            }
        }
        catch
        {
            // ProtectedLocalStorage can throw if JS is not available (e.g. prerendering)
            // Or if data is tampered with. We just fallback to unauthenticated.
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    public async Task LoginAsync(string homeserver, string username, string password)
    {
        await sessionService.LoginAsync(homeserver, username, password);
        
        if (sessionService.IsLoggedIn)
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
            
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
        }
    }

    public async Task LogoutAsync()
    {
        sessionService.Logout();
        await localStorage.DeleteAsync(StorageKey_Homeserver);
        await localStorage.DeleteAsync(StorageKey_AccessToken);
        
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
    }
}
