namespace SynapseAdmin.Models;

/// <summary>
/// Raised when a homeserver URL (entered, delegated or redirected) or the address it resolves to
/// is refused by the homeserver destination policy. The message is technical and must only be logged,
/// never shown to the user.
/// </summary>
public sealed class HomeserverNotAllowedException(string message) : Exception(message);
