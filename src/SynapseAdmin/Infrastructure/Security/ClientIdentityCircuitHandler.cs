using Microsoft.AspNetCore.Components.Server.Circuits;
using SynapseAdmin.Extensions;

namespace SynapseAdmin.Infrastructure.Security;

/// <summary>
/// Captures the client's remote IP into the circuit-scoped <see cref="ClientIdentityProvider"/> while the
/// SignalR connection's HttpContext is available (circuit start and every reconnect, which may come from a new address).
/// </summary>
public sealed class ClientIdentityCircuitHandler(
    ClientIdentityProvider clientIdentity,
    ILogger<ClientIdentityCircuitHandler> logger) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Capture(circuit);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Capture(circuit);
        return Task.CompletedTask;
    }

    private void Capture(Circuit? circuit)
    {
        if (!clientIdentity.CaptureFromCurrentConnection())
        {
            logger.LogWarning(
                "Could not determine the client IP for circuit {CircuitId}; login throttling falls back to a shared bucket. Check the reverse proxy / TRUSTED_PROXIES configuration.",
                circuit?.Id.SanitizeForLogging());
        }
    }
}
