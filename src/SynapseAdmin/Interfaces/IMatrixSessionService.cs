using LibMatrix.Homeservers;
using SynapseAdmin.Models;

namespace SynapseAdmin.Interfaces;

public interface IMatrixSessionService
{
    AuthenticatedHomeserverGeneric? AuthenticatedHomeserver { get; }
    bool IsLoggedIn { get; }
    Task<OperationResult> LoginAsync(string homeserver, string username, string password);
    Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken);
    Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false);
    void Logout();
}
