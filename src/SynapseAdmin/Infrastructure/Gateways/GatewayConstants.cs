namespace SynapseAdmin.Infrastructure.Gateways;

/// <summary>
/// Brand and version sentinels of the gateways, and the Synapse Admin API path that reports the server version.
/// These are <c>const</c> because <see cref="SynapseCompatibleAdminGateway"/> uses them as default parameter values.
/// </summary>
public static class GatewayConstants
{
    /// <summary>Server version when the homeserver reports none (or the probe fails).</summary>
    public const string UnknownVersion = "Unknown";

    /// <summary>Server brand when the homeserver doesn't identify itself.</summary>
    public const string GenericBrand = "Generic";

    public const string SynapseBrand = "Synapse";

    public const string ServerVersionPath = "/_synapse/admin/v1/server_version";
}
