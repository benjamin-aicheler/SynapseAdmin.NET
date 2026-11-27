using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Services;

/// <summary>
/// Redacting a user's events (#600). <c>POST /_synapse/admin/v1/user/{userId}/redact</c> only starts a background task
/// and returns a <c>redact_id</c>, whose status exists only by that ID, so the running redaction of each user is kept
/// in <see cref="IMemoryCache"/>, as <see cref="RoomService"/> does for history purges. The reason is personal text
/// that room members see, so it is never logged; the homeserver's error texts are logged, but never shown.
/// </summary>
public class UserRedactionService(IMatrixSessionService sessionService, ILogger<UserRedactionService> logger, IStringLocalizer<SharedResources> L, IMemoryCache memoryCache) : IUserRedactionService
{
    /// <summary>How many failed redactions a status log lists; the count is always logged.</summary>
    public const int MaxLoggedFailedRedactions = 20;

    // Synapse keeps a finished redaction's status for 24 hours (or until it restarts), so tracking it longer is useless.
    private static readonly MemoryCacheEntryOptions ActiveRedactionCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
    };

    // Guards writes of the tracked redaction IDs. Static, because every session's (scoped) service shares the
    // process-wide IMemoryCache, and IMemoryCache has no compare-and-remove of its own (#522).
    private static readonly Lock ActiveRedactionLock = new();

    public Task<OperationResult<string>> StartRedactionAsync(string userId, UserRedactionRequestViewModel request, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorRedactingUserEvents",
            ex => logger.LogError(ex, "Error starting the redaction of events of user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var rooms = request.AllRooms
                    ? []
                    : request.RoomIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();

                // An empty list would mean the user's rooms, which the admin didn't choose.
                if (!request.AllRooms && rooms.Count == 0)
                {
                    return OperationResult<string>.Failure(L["UserRedactionNoRoomsSelected"], Severity.Warning);
                }

                // Synapse treats 0 as its default of 1000 and refuses a negative limit.
                if (request.Limit < 1)
                {
                    return OperationResult<string>.Failure(L["UserRedactionInvalidLimit"], Severity.Warning);
                }

                var gatewayRequest = new SynapseAdminUserRedactRequest
                {
                    Rooms = rooms,
                    Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason,
                    Limit = request.Limit,
                    UseAdmin = request.UseAdmin
                };
                var result = await gateway.RedactUserEventsAsync(userId, gatewayRequest, token);
                if (string.IsNullOrWhiteSpace(result?.RedactId))
                {
                    logger.LogWarning("Redaction of events of user {UserId} started without returning a redact_id", userId.SanitizeForLogging());
                    return OperationResult<string>.Ok(string.Empty, L["UserRedactionStarted"]);
                }

                SetActiveRedactionId(gateway, userId, result.RedactId);
                logger.LogInformation("Started redaction {RedactId} of events of user {UserId} in {Scope}, limit {Limit}, as admin {UseAdmin}",
                    result.RedactId.SanitizeForLogging(), userId.SanitizeForLogging(),
                    request.AllRooms ? "the user's rooms" : $"{rooms.Count} selected rooms", request.Limit, request.UseAdmin);
                return OperationResult<string>.Ok(result.RedactId, L["UserRedactionStarted"]);
            });

    public Task<OperationResult<UserRedactionStatusViewModel>> GetRedactionStatusAsync(string redactId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingUserRedactionStatus",
            ex => logger.LogError(ex, "Error getting the status of redaction {RedactId}", redactId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetUserRedactionStatusAsync(redactId, token);
                // Null is an answer: the homeserver no longer knows the redaction (its status is kept 24 h, or until
                // it restarts). It isn't a failed check, so it doesn't count toward the page's cap.
                if (result == null)
                {
                    return OperationResult<UserRedactionStatusViewModel>.Ok(new UserRedactionStatusViewModel { RedactId = redactId, State = UserRedactionState.Unknown });
                }

                var status = result.ToViewModel(redactId);
                if (status.State == UserRedactionState.Failed)
                {
                    logger.LogWarning("Redaction {RedactId} failed: {Error}", redactId.SanitizeForLogging(), result.Error?.SanitizeForLogging());
                }
                else if (status.State == UserRedactionState.Complete && status.FailedRedactionCount > 0)
                {
                    logger.LogWarning("Redaction {RedactId} completed, but {FailedCount} events couldn't be redacted: {FailedRedactions}",
                        redactId.SanitizeForLogging(), status.FailedRedactionCount, DescribeFailures(result.FailedRedactions!));
                }
                return OperationResult<UserRedactionStatusViewModel>.Ok(status);
            },
            // Only the user page's poll reads this: a homeserver that refuses it works as intended, so the refusal isn't an error.
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support reading the status of redaction {RedactId}", redactId.SanitizeForLogging()));

    public string? GetActiveRedactionId(string userId)
    {
        var key = GetActiveRedactionCacheKey(sessionService.Gateway, userId);
        return key != null && memoryCache.TryGetValue<string>(key, out var redactId) ? redactId : null;
    }

    public void ClearActiveRedactionId(string userId, string expectedRedactId)
    {
        var key = GetActiveRedactionCacheKey(sessionService.Gateway, userId);
        if (key == null) return;
        // Compare and remove in one step: another session must not start a newer redaction in between (#522).
        lock (ActiveRedactionLock)
        {
            if (memoryCache.TryGetValue<string>(key, out var redactId) && redactId == expectedRedactId) memoryCache.Remove(key);
        }
    }

    private void SetActiveRedactionId(IMatrixGateway gateway, string userId, string redactId)
    {
        var key = GetActiveRedactionCacheKey(gateway, userId);
        if (key == null) return;
        lock (ActiveRedactionLock)
        {
            memoryCache.Set(key, redactId, ActiveRedactionCacheOptions);
        }
    }

    /// <summary>
    /// Scoped per homeserver, because every session in the process shares the cache (#431): the homeserver URL
    /// without a trailing slash, in lower case. <c>null</c> without a session or a user.
    /// </summary>
    private static string? GetActiveRedactionCacheKey(IMatrixGateway? gateway, string userId) =>
        gateway?.HomeserverUrl is { } homeserver && !string.IsNullOrEmpty(userId)
            ? $"active_redaction:{homeserver.TrimEnd('/').ToLowerInvariant()}:{userId}"
            : null;

    /// <summary>The first <see cref="MaxLoggedFailedRedactions"/> event IDs and their errors, each sanitized.</summary>
    private static string DescribeFailures(Dictionary<string, string> failures) =>
        string.Join("; ", failures.Take(MaxLoggedFailedRedactions)
            .Select(f => $"{f.Key.SanitizeForLogging(255)}: {f.Value.SanitizeForLogging(255)}"));
}
