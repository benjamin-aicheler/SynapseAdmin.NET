using MudBlazor;
using SynapseAdmin.Models;

namespace SynapseAdmin.Components.Shared;

/// <summary>
/// Follows a long-running homeserver operation by checking its status (#535): <c>RoomDetails</c> uses it for a history
/// purge and a room deletion. It checks at once, then every <see cref="IntervalMs"/>, until <see cref="Apply"/> says the
/// operation ended, the poll is no longer current, the token is cancelled, or <see cref="MaxConsecutiveFailures"/>
/// checks in a row fail.
/// </summary>
/// <remarks>
/// Answers are applied on the component's dispatcher (<see cref="InvokeAsync"/>), and only while <see cref="IsCurrent"/>
/// holds, so a late answer for another room or an older operation changes nothing (#522). The caller owns the
/// cancellation token and writes the log messages, because their templates differ per operation.
/// </remarks>
internal sealed class StatusPoller<TStatus> where TStatus : class
{
    /// <summary>Reads the status once.</summary>
    public required Func<CancellationToken, Task<OperationResult<TStatus>>> FetchAsync { get; init; }

    /// <summary>The component's <c>InvokeAsync</c>: runs an action on its dispatcher.</summary>
    public required Func<Action, Task> InvokeAsync { get; init; }

    /// <summary>Whether an answer still belongs to the operation shown. Called on the dispatcher.</summary>
    public required Func<bool> IsCurrent { get; init; }

    /// <summary>
    /// Shows a status and returns whether the operation ended, so checking stops. Called on the dispatcher, only while
    /// <see cref="IsCurrent"/> holds.
    /// </summary>
    public required Func<TStatus, bool> Apply { get; init; }

    /// <summary>
    /// Stops showing the operation and reports that its status can't be read. Called on the dispatcher, only while
    /// <see cref="IsCurrent"/> holds.
    /// </summary>
    public required Action GiveUp { get; init; }

    public required int IntervalMs { get; init; }

    public required int MaxConsecutiveFailures { get; init; }

    /// <summary>Logs a failed check, with its attempt number and the result's message.</summary>
    public required Action<int, string> LogFailedAttempt { get; init; }

    /// <summary>Logs an exception that ends the loop; <see cref="GiveUp"/> runs after it.</summary>
    public required Action<Exception> LogUnexpectedError { get; init; }

    /// <summary>Logs a failure of <see cref="GiveUp"/> after an unexpected error, for example because the circuit is gone.</summary>
    public required Action<Exception> LogCleanupFailure { get; init; }

    public async Task RunAsync(CancellationToken token)
    {
        var consecutiveFailures = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await FetchAsync(token);
                if (result.Success && result.Data != null)
                {
                    consecutiveFailures = 0;
                    var status = result.Data;
                    // An answer that is no longer current ends this poll: the page moved on, or follows a newer operation.
                    var shouldStop = true;

                    await InvokeAsync(() =>
                    {
                        if (IsCurrent()) shouldStop = Apply(status);
                    });

                    if (shouldStop) break;
                }
                else
                {
                    // A check cancelled because the page moved on is not a failed attempt.
                    if (result.Severity == Severity.Normal && token.IsCancellationRequested) break;

                    consecutiveFailures++;
                    LogFailedAttempt(consecutiveFailures, result.Message ?? "Unknown error");

                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        await InvokeAsync(GiveUpIfCurrent);
                        break;
                    }
                }

                await Task.Delay(IntervalMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // The page moved on or was disposed.
        }
        catch (Exception ex)
        {
            LogUnexpectedError(ex);
            try
            {
                await InvokeAsync(GiveUpIfCurrent);
            }
            catch (Exception cleanupEx)
            {
                // Prevent a secondary crash during cleanup.
                LogCleanupFailure(cleanupEx);
            }
        }
    }

    private void GiveUpIfCurrent()
    {
        if (IsCurrent()) GiveUp();
    }
}
