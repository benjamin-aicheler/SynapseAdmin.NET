using Microsoft.AspNetCore.Http;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Infrastructure.Security;

/// <summary>
/// Scoped (per-circuit) client identity.
/// <para>
/// In interactive Blazor Server code, <see cref="IHttpContextAccessor.HttpContext"/> is not guaranteed to be
/// available (or may belong to a different request). <see cref="ClientIdentityCircuitHandler"/> therefore captures
/// the remote IP of the SignalR connection when the circuit opens / reconnects, where the connection's
/// <see cref="HttpContext"/> is reliably present. Outside a circuit (controllers, tests) this falls back to the
/// ambient <see cref="HttpContext"/>.
/// </para>
/// </summary>
public sealed class ClientIdentityProvider(IHttpContextAccessor httpContextAccessor) : IClientIdentityProvider
{
    private volatile string? _capturedClientIp;

    public string? GetClientIp() =>
        _capturedClientIp ?? ReadRemoteIp(httpContextAccessor.HttpContext);

    /// <summary>
    /// Captures the remote IP from the current connection's <see cref="HttpContext"/>, if available.
    /// Called by <see cref="ClientIdentityCircuitHandler"/>. Returns whether an IP was captured.
    /// </summary>
    internal bool CaptureFromCurrentConnection()
    {
        var ip = ReadRemoteIp(httpContextAccessor.HttpContext);
        if (ip == null) return false;

        _capturedClientIp = ip;
        return true;
    }

    private static string? ReadRemoteIp(HttpContext? context) =>
        context?.Connection?.RemoteIpAddress?.ToString();
}
