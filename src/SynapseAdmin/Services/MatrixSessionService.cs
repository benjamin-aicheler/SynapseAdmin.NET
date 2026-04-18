using LibMatrix.Homeservers;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Infrastructure.Gateways;

namespace SynapseAdmin.Services;

public class MatrixSessionService(IMatrixAuthGateway authGateway, ILogger<MatrixSessionService> logger, IStringLocalizer<SharedResources> L) : IMatrixSessionService
{
    public AuthenticatedHomeserverGeneric? AuthenticatedHomeserver { get; private set; }
    public IMatrixGateway? Gateway { get; private set; }

    public bool IsLoggedIn => AuthenticatedHomeserver != null;

    public async Task<OperationResult> LoginAsync(string homeserver, string username, string password)
    {
        try
        {
            // Resolve homeserver via gateway
            await authGateway.ResolveHomeserverAsync(homeserver);

            var loginResponse = await authGateway.LoginAsync(homeserver, username, password);
            AuthenticatedHomeserver = await authGateway.GetAuthenticatedAsync(homeserver, loginResponse.AccessToken);
            
            InitializeGateway();

            logger.LogInformation("User {Username} successfully logged into {Homeserver}", username.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok(L["LoginSuccessful"]);
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

            AuthenticatedHomeserver = await authGateway.GetAuthenticatedAsync(homeserver, accessToken);
            
            InitializeGateway();

            logger.LogInformation("Session successfully logged in via token for user {UserId} on {Homeserver}", AuthenticatedHomeserver.UserId.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok(L["LoginSuccessful"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login with token failed for {Homeserver}", homeserver.SanitizeForLogging());
            AuthenticatedHomeserver = null;
            Gateway = null;
            return OperationResult.Failure(L["LoginFailed"]);
        }
    }

    public async Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false)
    {
        if (!force && AuthenticatedHomeserver != null && 
            AuthenticatedHomeserver.BaseUrl == homeserver && 
            AuthenticatedHomeserver.AccessToken == accessToken)
        {
            return OperationResult.Ok();
        }

        try
        {
            // Resolve homeserver via gateway
            await authGateway.ResolveHomeserverAsync(homeserver);

            AuthenticatedHomeserver = await authGateway.GetAuthenticatedAsync(homeserver, accessToken);
            
            InitializeGateway();

            logger.LogInformation("Session successfully restored for user {UserId} on {Homeserver}", AuthenticatedHomeserver.UserId.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore session for {Homeserver}", homeserver.SanitizeForLogging());
            AuthenticatedHomeserver = null;
            Gateway = null;
            return OperationResult.Failure(L["ErrorLoadingTokens"]);
        }
    }

    public void Logout()
    {
        if (AuthenticatedHomeserver != null)
        {
            logger.LogInformation("User {UserId} logged out from {Homeserver}", AuthenticatedHomeserver.UserId.SanitizeForLogging(), AuthenticatedHomeserver.ServerName.SanitizeForLogging());
        }
        AuthenticatedHomeserver = null;
        Gateway = null;
    }

    private void InitializeGateway()
    {
        if (AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapse)
        {
            Gateway = new SynapseAdminGateway(synapse);
        }
        else if (AuthenticatedHomeserver != null)
        {
            // Fallback to a generic gateway if not synapse (to be implemented if needed)
            // For now, we only support Synapse
            Gateway = null; 
        }
    }
}
