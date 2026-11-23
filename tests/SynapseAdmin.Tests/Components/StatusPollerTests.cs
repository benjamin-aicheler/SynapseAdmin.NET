using SynapseAdmin.Components.Shared;
using SynapseAdmin.Models;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The status loop that <c>RoomDetails</c> uses for a history purge and a room deletion (#535). The page tests cover it
/// through both operations with the real 5 s interval; these run it with a 1 ms interval, which makes long sequences such
/// as a success between failures cheap to test.
/// </summary>
public class StatusPollerTests
{
    private sealed class Run
    {
        public List<string> Applied { get; } = [];
        public List<int> FailedAttempts { get; } = [];
        public List<Exception> UnexpectedErrors { get; } = [];
        public List<Exception> CleanupFailures { get; } = [];
        public int Fetches { get; set; }
        public int GiveUps { get; set; }
        public bool Current { get; set; } = true;
    }

    /// <summary>
    /// A poller over <paramref name="answers"/>, one per check; "fail" is a failed result, "stop" ends the operation, any
    /// other text keeps it running. The dispatcher runs each action at once.
    /// </summary>
    private static StatusPoller<string> Poller(Run run, Func<int, Task<OperationResult<string>>> answer, Action? giveUp = null) => new()
    {
        FetchAsync = _ => answer(run.Fetches++),
        InvokeAsync = action =>
        {
            action();
            return Task.CompletedTask;
        },
        IsCurrent = () => run.Current,
        Apply = status =>
        {
            run.Applied.Add(status);
            return status == "stop";
        },
        GiveUp = giveUp ?? (() => run.GiveUps++),
        IntervalMs = 1,
        MaxConsecutiveFailures = 5,
        LogFailedAttempt = (attempt, _) => run.FailedAttempts.Add(attempt),
        LogUnexpectedError = run.UnexpectedErrors.Add,
        LogCleanupFailure = run.CleanupFailures.Add
    };

    private static Func<int, Task<OperationResult<string>>> Answers(params string[] answers) => i => Task.FromResult(
        answers[i] == "fail" ? OperationResult<string>.Failure("status unavailable") : OperationResult<string>.Ok(answers[i]));

    [Fact]
    public async Task ChecksUntilApplySaysTheOperationEnded()
    {
        var run = new Run();

        await Poller(run, Answers("scheduled", "active", "stop")).RunAsync(CancellationToken.None);

        Assert.Equal(["scheduled", "active", "stop"], run.Applied);
        Assert.Equal(3, run.Fetches);
        Assert.Equal(0, run.GiveUps);
    }

    [Fact]
    public async Task FiveFailedChecksInARow_AreLogged_ThenGiveUp()
    {
        var run = new Run();

        await Poller(run, Answers("fail", "fail", "fail", "fail", "fail", "never asked")).RunAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3, 4, 5], run.FailedAttempts);
        Assert.Equal(5, run.Fetches);
        Assert.Equal(1, run.GiveUps);
        Assert.Empty(run.Applied);
    }

    /// <summary>The cap counts failures in a row: a status answer in between starts the count again.</summary>
    [Fact]
    public async Task ASuccessfulCheck_ResetsTheFailureCount()
    {
        var run = new Run();

        await Poller(run, Answers("fail", "fail", "fail", "fail", "active", "fail", "fail", "fail", "fail", "stop"))
            .RunAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3, 4, 1, 2, 3, 4], run.FailedAttempts);
        Assert.Equal(["active", "stop"], run.Applied);
        Assert.Equal(0, run.GiveUps);
    }

    /// <summary>An answer that no longer belongs to the operation shown is not applied, and ends the poll.</summary>
    [Fact]
    public async Task AnswerThatIsNoLongerCurrent_IsNotApplied_AndEndsThePoll()
    {
        var run = new Run { Current = false };

        await Poller(run, Answers("active", "never asked")).RunAsync(CancellationToken.None);

        Assert.Empty(run.Applied);
        Assert.Equal(1, run.Fetches);
    }

    [Fact]
    public async Task GivingUp_OnlyWhileCurrent()
    {
        var run = new Run { Current = false };

        await Poller(run, Answers("fail", "fail", "fail", "fail", "fail")).RunAsync(CancellationToken.None);

        Assert.Equal(5, run.FailedAttempts.Count);
        Assert.Equal(0, run.GiveUps);
    }

    /// <summary>A check cancelled because the page moved on is not a failed attempt.</summary>
    [Fact]
    public async Task CancelledAnswerAfterCancellation_IsNotAFailedAttempt()
    {
        using var cts = new CancellationTokenSource();
        var run = new Run();

        await Poller(run, _ =>
        {
            cts.Cancel();
            return Task.FromResult(OperationResult<string>.Cancelled());
        }).RunAsync(cts.Token);

        Assert.Empty(run.FailedAttempts);
        Assert.Equal((1, 0), (run.Fetches, run.GiveUps));
    }

    /// <summary>A cancelled answer while the poll still runs counts as a failed check, as any other failure.</summary>
    [Fact]
    public async Task CancelledAnswerWithoutCancellation_IsAFailedAttempt()
    {
        var run = new Run();

        await Poller(run, i => Task.FromResult(i == 0 ? OperationResult<string>.Cancelled() : OperationResult<string>.Ok("stop")))
            .RunAsync(CancellationToken.None);

        Assert.Equal([1], run.FailedAttempts);
        Assert.Equal(["stop"], run.Applied);
    }

    [Fact]
    public async Task AlreadyCancelled_ChecksNothing()
    {
        var run = new Run();

        await Poller(run, Answers("active")).RunAsync(new CancellationToken(canceled: true));

        Assert.Equal(0, run.Fetches);
    }

    [Fact]
    public async Task UnexpectedError_IsLogged_ThenGivesUp()
    {
        var run = new Run();
        var error = new InvalidOperationException("boom");

        await Poller(run, _ => Task.FromException<OperationResult<string>>(error)).RunAsync(CancellationToken.None);

        Assert.Same(error, Assert.Single(run.UnexpectedErrors));
        Assert.Equal(1, run.GiveUps);
        Assert.Empty(run.CleanupFailures);
    }

    [Fact]
    public async Task FailingGiveUpAfterAnUnexpectedError_IsLoggedAsACleanupFailure()
    {
        var run = new Run();
        var error = new InvalidOperationException("boom");
        var cleanupFailure = new InvalidOperationException("circuit gone");

        await Poller(run, _ => Task.FromException<OperationResult<string>>(error), giveUp: () => throw cleanupFailure)
            .RunAsync(CancellationToken.None);

        Assert.Same(error, Assert.Single(run.UnexpectedErrors));
        Assert.Same(cleanupFailure, Assert.Single(run.CleanupFailures));
    }
}
