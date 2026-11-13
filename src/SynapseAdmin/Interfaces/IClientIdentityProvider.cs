namespace SynapseAdmin.Interfaces;

/// <summary>
/// Provides a reliable client identity (remote IP) for the current Blazor circuit or HTTP request.
/// Used to partition per-client protections such as login rate limiting.
/// </summary>
public interface IClientIdentityProvider
{
    /// <summary>
    /// The client's remote IP address (after forwarded-headers processing), or <c>null</c> if it cannot be determined.
    /// </summary>
    string? GetClientIp();
}
