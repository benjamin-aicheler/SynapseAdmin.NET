namespace SynapseAdmin.Infrastructure.Security;

/// <summary>
/// Custom claim types of an authenticated Matrix session. Single source for <c>AuthController</c> (cookie sign-in),
/// <c>MatrixAuthenticationStateProvider</c> (circuit authentication state) and <c>MediaController</c> (session restore).
/// The values are persisted inside issued authentication cookies, so they must never change.
/// </summary>
public static class MatrixClaimTypes
{
    public const string Homeserver = "Homeserver";
    public const string AccessToken = "AccessToken";
}
