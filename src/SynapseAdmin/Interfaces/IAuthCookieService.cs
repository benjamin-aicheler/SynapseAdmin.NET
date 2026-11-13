using SynapseAdmin.Models;

namespace SynapseAdmin.Interfaces;

/// <summary>
/// Establishes the HTTP authentication cookie (used by controllers such as media streaming) for the
/// Blazor session of the current circuit, via a single-use, browser-bound session bridge.
/// </summary>
public interface IAuthCookieService
{
    Task<OperationResult> EstablishCookieSessionAsync(string homeserver, string accessToken, string userId, string username);
}
