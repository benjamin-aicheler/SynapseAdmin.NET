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
                var restoreResult = await sessionService.RestoreSessionAsync(homeserverResult.Value, tokenResult.Value);
                
                if (restoreResult.Success && sessionService.IsLoggedIn)
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
                else
                {
                    // If session restoration failed, clean up storage
                    await localStorage.DeleteAsync(StorageKey_Homeserver);
                    await localStorage.DeleteAsync(StorageKey_AccessToken);
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
            
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
        }
        else
        {
            // If we want to pass the error back through this provider, 
            // we'd need to change LoginAsync's return type too.
            // For now, the caller might still expect an exception or check the session state.
            // To maintain compatibility with Login.razor.cs's catch block, we can throw if it fails.
            throw new Exception(result.ErrorMessage ?? "Login failed");
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
