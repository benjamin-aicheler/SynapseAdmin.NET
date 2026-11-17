using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using SynapseAdmin.Models;

namespace SynapseAdmin.Interfaces;

/// <summary>
/// The Blazor authentication state of the current circuit: logs in to and out of the Matrix session and exposes the
/// session's identity for the cookie bridge. It is the same scoped instance as Blazor's
/// <see cref="AuthenticationStateProvider"/>, so a login or logout updates the cascading authentication state.
/// </summary>
public interface IMatrixAuthStateService
{
    Task<AuthenticationState> GetAuthenticationStateAsync();
    Task<OperationResult> LoginAsync(string homeserver, string username, string password);
    Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken);
    Task LogoutAsync(NavigationManager? navigation = null);
    string? GetAccessToken();
    string? GetUserId();
    string? GetUsername();
    string? GetHomeserver();
}
