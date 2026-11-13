using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

/// <summary>
/// Circuit-side half of the cookie bridge. The bridge is bound to this browser's antiforgery request token
/// (from <see cref="AntiforgeryStateProvider"/>, persisted into the circuit from the initial page request), and
/// the key is redeemed by a same-origin <c>fetch</c> POST carrying that token, so the key never appears in a URL.
/// </summary>
public sealed class AuthCookieService(
    ISessionBridgeService bridgeService,
    AntiforgeryStateProvider antiforgeryStateProvider,
    IOptions<AntiforgeryOptions> antiforgeryOptions,
    IJSRuntime jsRuntime,
    ILogger<AuthCookieService> logger,
    IStringLocalizer<SharedResources> L) : IAuthCookieService
{
    private const string ModulePath = "./js/auth-bridge.js";

    public async Task<OperationResult> EstablishCookieSessionAsync(string homeserver, string accessToken, string userId, string username)
    {
        var requestToken = antiforgeryStateProvider.GetAntiforgeryToken()?.Value;
        var headerName = antiforgeryOptions.Value.HeaderName;
        if (string.IsNullOrEmpty(requestToken) || string.IsNullOrEmpty(headerName))
        {
            logger.LogError("Cannot establish the authentication cookie: no antiforgery token is available for this circuit.");
            return OperationResult.Failure(L["ErrorSessionBridgeFailed"]);
        }

        var key = bridgeService.CreateBridge(homeserver, accessToken, userId, username, requestToken);

        try
        {
            await using var module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            var accepted = await module.InvokeAsync<bool>("completeSignIn", key, requestToken, headerName);
            if (accepted)
            {
                return OperationResult.Ok();
            }

            logger.LogWarning("The sign-in endpoint rejected the session bridge exchange.");
            return OperationResult.Failure(L["ErrorSessionBridgeFailed"]);
        }
        catch (Exception ex) when (ex is OperationCanceledException or JSDisconnectedException or TaskCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (JSException ex)
        {
            logger.LogError(ex, "Session bridge exchange failed in the browser.");
            return OperationResult.Failure(L["ErrorSessionBridgeFailed"]);
        }
    }
}
