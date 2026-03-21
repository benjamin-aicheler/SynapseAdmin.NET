using LibMatrix.Homeservers;
using LibMatrix.Services;

namespace SynapseAdmin.Services;

public class MatrixSessionService(HomeserverProviderService hsProvider)
{
    public AuthenticatedHomeserverGeneric? AuthenticatedHomeserver { get; private set; }

    public bool IsLoggedIn => AuthenticatedHomeserver != null;

    public async Task LoginAsync(string homeserver, string username, string password)
    {
        var loginResponse = await hsProvider.Login(homeserver, username, password);
        AuthenticatedHomeserver = await hsProvider.GetAuthenticatedWithToken(homeserver, loginResponse.AccessToken);
    }

    public async Task RestoreSessionAsync(string homeserver, string accessToken)
    {
        AuthenticatedHomeserver = await hsProvider.GetAuthenticatedWithToken(homeserver, accessToken);
    }

    public void Logout()
    {
        AuthenticatedHomeserver = null;
    }
}

