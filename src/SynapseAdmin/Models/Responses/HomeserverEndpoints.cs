namespace SynapseAdmin.Models.Responses;

/// <summary>
/// Policy-validated homeserver endpoints produced by discovery.
/// </summary>
/// <param name="ClientUrl">Client-server API base URL (validated).</param>
/// <param name="ServerUrl">Federation base URL, or <c>null</c> if federation is not reachable.</param>
/// <param name="ServerSoftwareName">Software name reported by <c>/_matrix/federation/v1/version</c>, if available.</param>
/// <param name="ServerSoftwareVersion">Software version reported by <c>/_matrix/federation/v1/version</c>, if available.</param>
public sealed record HomeserverEndpoints(
    string ClientUrl,
    string? ServerUrl,
    string? ServerSoftwareName,
    string? ServerSoftwareVersion);
