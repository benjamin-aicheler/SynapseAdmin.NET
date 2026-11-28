namespace SynapseAdmin.Models;

/// <summary>
/// Raised by <see cref="Interfaces.Gateways.IMatrixAuthGateway"/> when the homeserver refuses a login because of its
/// own rate limit (HTTP <c>429</c> or <c>M_LIMIT_EXCEEDED</c>). The gateway throws it in place of the SDK's or the HTTP
/// stack's exception, which it keeps as <see cref="Exception.InnerException"/>, so that callers don't depend on
/// <c>LibMatrix</c>. The message is technical and must only be logged, never shown to the user.
/// </summary>
public sealed class HomeserverRateLimitedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
