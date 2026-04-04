using LibMatrix.Homeservers;
using LibMatrix.Services;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;

namespace SynapseAdmin.Services;

public class MatrixSessionService(HomeserverProviderService hsProvider, ILogger<MatrixSessionService> logger, IStringLocalizer<SharedResources> L) : IMatrixSessionService
{
    public AuthenticatedHomeserverGeneric? AuthenticatedHomeserver { get; private set; }

    public bool IsLoggedIn => AuthenticatedHomeserver != null;

    public async Task<OperationResult> LoginAsync(string homeserver, string username, string password)
    {
        try
        {
            // Explicitly resolve homeserver with federation support to ensure correct server type detection
            await hsProvider.GetRemoteHomeserver(homeserver, enableServer: true);

            var loginResponse = await hsProvider.Login(homeserver, username, password);
            AuthenticatedHomeserver = await hsProvider.GetAuthenticatedWithToken(homeserver, loginResponse.AccessToken);
            logger.LogInformation("User {Username} successfully logged into {Homeserver}", username, homeserver);
            return OperationResult.Ok(L["LoginSuccessful"] ?? "Login successful.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login failed for user {Username} on {Homeserver}", username, homeserver);
            return OperationResult.Failure(ex.Message);
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
            // Explicitly resolve homeserver with federation support to ensure correct server type detection
            await hsProvider.GetRemoteHomeserver(homeserver, enableServer: true);

            AuthenticatedHomeserver = await hsProvider.GetAuthenticatedWithToken(homeserver, accessToken);
            logger.LogInformation("Session successfully restored for user {UserId} on {Homeserver}", AuthenticatedHomeserver.UserId, homeserver);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore session for {Homeserver}", homeserver);
            AuthenticatedHomeserver = null;
            return OperationResult.Failure(string.Format(L["ErrorLoadingTokens"], ex.Message));
        }
    }

    public void Logout()
    {
        if (AuthenticatedHomeserver != null)
        {
            logger.LogInformation("User {UserId} logged out from {Homeserver}", AuthenticatedHomeserver.UserId, AuthenticatedHomeserver.ServerName);
        }
        AuthenticatedHomeserver = null;
    }
}
