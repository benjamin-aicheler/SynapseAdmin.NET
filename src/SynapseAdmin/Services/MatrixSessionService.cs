using LibMatrix.Homeservers;
using LibMatrix.Services;
using SynapseAdmin.Models;

namespace SynapseAdmin.Services;

public class MatrixSessionService(HomeserverProviderService hsProvider, ILogger<MatrixSessionService> logger)
{
    public AuthenticatedHomeserverGeneric? AuthenticatedHomeserver { get; private set; }

    public bool IsLoggedIn => AuthenticatedHomeserver != null;

    public async Task<OperationResult> LoginAsync(string homeserver, string username, string password)
    {
        try
        {
            var loginResponse = await hsProvider.Login(homeserver, username, password);
            AuthenticatedHomeserver = await hsProvider.GetAuthenticatedWithToken(homeserver, loginResponse.AccessToken);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login failed for user {Username} on {Homeserver}", username, homeserver);
            return OperationResult.Failure(ex.Message);
        }
    }

    public async Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken)
    {
        try
        {
            AuthenticatedHomeserver = await hsProvider.GetAuthenticatedWithToken(homeserver, accessToken);
            // Verify if the token is actually valid by doing a simple call if needed, 
            // but GetAuthenticatedWithToken usually throws or returns a client that will fail later.
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore session for {Homeserver}", homeserver);
            AuthenticatedHomeserver = null;
            return OperationResult.Failure(ex.Message);
        }
    }

    public void Logout()
    {
        AuthenticatedHomeserver = null;
    }
}
