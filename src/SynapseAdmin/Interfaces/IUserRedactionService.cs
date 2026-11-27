using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

/// <summary>
/// Redacting a user's events in a background task of the homeserver, and following it (#600). The running redaction of
/// each user is tracked per homeserver, so a reopened user page shows it again.
/// </summary>
public interface IUserRedactionService
{
    /// <summary>
    /// Starts the redaction and returns its <c>redact_id</c>, which is tracked for the user. An empty room selection
    /// or a limit below 1 fails with a warning, and nothing is sent. A homeserver that answers without an ID returns an
    /// empty string, and nothing is tracked.
    /// </summary>
    Task<OperationResult<string>> StartRedactionAsync(string userId, UserRedactionRequestViewModel request, CancellationToken token = default);

    /// <summary>
    /// Reads a redaction's status. A redaction the homeserver doesn't know is <see cref="UserRedactionState.Unknown"/>,
    /// not a failure. The homeserver's error texts are only logged.
    /// </summary>
    Task<OperationResult<UserRedactionStatusViewModel>> GetRedactionStatusAsync(string redactId, CancellationToken token = default);

    /// <summary>The tracked redaction of the user on this session's homeserver, or <c>null</c>.</summary>
    string? GetActiveRedactionId(string userId);

    /// <summary>Stops tracking the user's redaction, but only if it is still <paramref name="expectedRedactId"/>.</summary>
    void ClearActiveRedactionId(string userId, string expectedRedactId);
}
