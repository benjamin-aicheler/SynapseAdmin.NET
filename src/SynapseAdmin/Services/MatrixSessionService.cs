using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces.Gateways;

namespace SynapseAdmin.Services;

public class MatrixSessionService(IMatrixAuthGateway authGateway, ILogger<MatrixSessionService> logger, IStringLocalizer<SharedResources> L) : IMatrixSessionService
{
    public IMatrixGateway? Gateway { get; private set; }

    public bool IsLoggedIn => Gateway != null;

    public async Task<OperationResult> LoginAsync(string homeserver, string username, string password)
    {
        try
        {
            // Resolve homeserver via gateway
            await authGateway.ResolveHomeserverAsync(homeserver);

            var loginResponse = await authGateway.LoginAsync(homeserver, username, password);
            Gateway = await authGateway.GetAuthenticatedAsync(homeserver, loginResponse.AccessToken);
            
            logger.LogInformation("User {Username} successfully logged into {Homeserver}", username.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok(L["LoginSuccessful"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login failed for user {Username} on {Homeserver}", username.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Failure(L["LoginFailed"]);
        }
    }

    public async Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken)
    {
        try
        {
            // Resolve homeserver via gateway
            await authGateway.ResolveHomeserverAsync(homeserver);

            Gateway = await authGateway.GetAuthenticatedAsync(homeserver, accessToken);
            
            logger.LogInformation("Session successfully logged in via token for user {UserId} on {Homeserver}", Gateway.UserId.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok(L["LoginSuccessful"]);
        }
        catch (OperationCanceledException)
        {
            Gateway = null;
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login with token failed for {Homeserver}", homeserver.SanitizeForLogging());
            Gateway = null;
            return OperationResult.Failure(L["LoginFailed"]);
        }
    }

    public async Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false)
    {
        if (!force && Gateway != null && 
            Gateway.HomeserverUrl == homeserver && 
            Gateway.AccessToken == accessToken)
        {
            return OperationResult.Ok();
        }

        try
        {
            // Resolve homeserver via gateway
            await authGateway.ResolveHomeserverAsync(homeserver);

            Gateway = await authGateway.GetAuthenticatedAsync(homeserver, accessToken);
            
            logger.LogInformation("Session successfully restored for user {UserId} on {Homeserver}", Gateway.UserId.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            Gateway = null;
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore session for {Homeserver}", homeserver.SanitizeForLogging());
            Gateway = null;
            return OperationResult.Failure(L["ErrorLoadingTokens"]);
        }
    }

    public void Logout()
    {
        if (Gateway != null)
        {
            logger.LogInformation("User {UserId} logged out from {Homeserver}", Gateway.UserId.SanitizeForLogging(), Gateway.ServerName.SanitizeForLogging());
        }
        Gateway = null;
    }
}
