using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Room deletion on the room page (#518, AC-11): the homeserver only starts a deletion, so the page stays open, shows a
/// banner while the deletion is scheduled or running, checks it every 5 s and reports how it ended.
/// </summary>
public class RoomDetailsRoomDeletionTests : DetailPageTestBase
{
    private const string RoomA = "!a:hs";
    private const string RoomB = "!b:hs";
    private const string Requested = "Deletion requested";

    private readonly Stub _rooms;
    private OperationResult<string> _deleteResult = OperationResult<string>.Ok("delete-1", Requested);
    private Func<string, Task<OperationResult<RoomDeletionStatusViewModel?>>> _running = _ => NoRunningDeletion();
    private Func<string, Task<OperationResult<RoomDeletionStatusViewModel>>> _status = _ => Pending<OperationResult<RoomDeletionStatusViewModel>>().Task;

    public RoomDetailsRoomDeletionTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), args => Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(
                new RoomDetailViewModel { RoomId = (string)args[0]!, Name = NameOf((string)args[0]!) })))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ =>
                Task.FromResult(OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel())))
            .On(nameof(IRoomService.DeleteRoomAsync), _ => Task.FromResult(_deleteResult))
            .On(nameof(IRoomService.GetActiveRoomDeletionAsync), args => _running((string)args[0]!))
            .On(nameof(IRoomService.GetRoomDeletionStatusAsync), args => _status((string)args[0]!));
    }

    private static string NameOf(string roomId) => roomId == RoomA ? "Alpha Lounge" : "Bravo Lounge";

    private static Task<OperationResult<RoomDeletionStatusViewModel?>> NoRunningDeletion() =>
        Task.FromResult(OperationResult<RoomDeletionStatusViewModel?>.Ok(null));

    private static Task<OperationResult<RoomDeletionStatusViewModel?>> RunningDeletion(string deleteId, RoomDeletionState state) =>
        Task.FromResult(OperationResult<RoomDeletionStatusViewModel?>.Ok(new RoomDeletionStatusViewModel { DeleteId = deleteId, RoomId = RoomA, State = state }));

    private static OperationResult<RoomDeletionStatusViewModel> Status(RoomDeletionState state, string deleteId = "delete-1") =>
        OperationResult<RoomDeletionStatusViewModel>.Ok(new RoomDeletionStatusViewModel { DeleteId = deleteId, RoomId = RoomA, State = state });

    private IStringLocalizer<SharedResources> Localizer => Services.GetRequiredService<IStringLocalizer<SharedResources>>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();
    private IReadOnlyList<object?[]> StatusCalls => _rooms.CallsTo(nameof(IRoomService.GetRoomDeletionStatusAsync));
    private IReadOnlyList<object?[]> SnackbarCalls => SnackbarStub.CallsTo(nameof(ISnackbar.Add));
    private string Banner => Localizer["RoomDeletionInProgress"].Value;
    private string ScheduledText => Localizer["RoomDeletionScheduledDescription"].Value;
    private string ActiveText => Localizer["RoomDeletionActiveDescription"].Value;

    // Blazor leaves non-ASCII characters unescaped but encodes quotes, so compare the decoded markup.
    private static string Markup(IRenderedComponent<RoomDetails> cut) => WebUtility.HtmlDecode(cut.Markup);

    private IRenderedComponent<RoomDetails> RenderRoom(string roomId = RoomA)
    {
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, roomId));
        cut.WaitForAssertion(() => Assert.Contains(NameOf(roomId), cut.Markup));
        return cut;
    }

    private static void Navigate(IRenderedComponent<RoomDetails> cut, string roomId)
    {
        cut.Render(p => p.Add(x => x.RoomId, roomId));
        cut.WaitForAssertion(() => Assert.Contains(NameOf(roomId), cut.Markup));
    }

    private async Task DeleteAsync(IRenderedComponent<RoomDetails> cut, string handler = "DeleteRoom")
    {
        var count = Confirmations.Count;
        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Equal(count + 1, Confirmations.Count));
        Confirmations[^1].Result.SetResult(true);
        await action;
    }

    private void AssertSnackbars(params (string Message, Severity Severity)[] expected) =>
        Assert.Equal(expected, SnackbarCalls.Select(a => ((string)a[0]!, (Severity)a[1]!)));

    // ---- Requesting a deletion ---------------------------------------------------------------------

    [Theory]
    [InlineData("DeleteRoom")]
    [InlineData("DeleteAndBlockRoom")]
    public async Task Delete_StaysOnTheRoomPage_ShowsTheBanner_AndChecksTheDeletion(string handler)
    {
        var cut = RenderRoom();
        var roomUri = Navigation.Uri;

        await DeleteAsync(cut, handler);

        Assert.Equal(roomUri, Navigation.Uri);
        AssertSnackbars((Requested, Severity.Success));
        cut.WaitForAssertion(() => Assert.Contains(ScheduledText, Markup(cut)));
        Assert.Contains(Banner, Markup(cut));
        Assert.Contains("Alpha Lounge", cut.Markup);
        cut.WaitForAssertion(() => Assert.Equal("delete-1", Assert.Single(StatusCalls)[0]));
    }

    [Fact]
    public async Task Delete_WithoutDeleteId_StaysOnThePage_WithoutABanner()
    {
        _deleteResult = OperationResult<string>.Ok(string.Empty, Requested);
        var cut = RenderRoom();
        var roomUri = Navigation.Uri;

        await DeleteAsync(cut);
        await Task.Delay(300); // a poll would start on a background task

        Assert.Equal(roomUri, Navigation.Uri);
        AssertSnackbars((Requested, Severity.Success));
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(StatusCalls);
    }

    [Fact]
    public async Task Delete_Failure_ShowsTheError_WithoutABanner()
    {
        _deleteResult = OperationResult<string>.Failure("Delete failed");
        var cut = RenderRoom();

        await DeleteAsync(cut);
        await Task.Delay(300);

        AssertSnackbars(("Delete failed", Severity.Error));
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(StatusCalls);
    }

    /// <summary>A homeserver that refuses room deletion (Tuwunel, #559): the warning, and nothing to follow.</summary>
    [Theory]
    [InlineData("DeleteRoom")]
    [InlineData("DeleteAndBlockRoom")]
    public async Task Delete_NotSupported_ShowsTheWarning_KeepsTheRoom_WithoutABanner(string handler)
    {
        var notSupported = Localizer["OperationNotSupportedByHomeserver"].Value;
        _deleteResult = OperationResult<string>.Failure(notSupported, Severity.Warning);
        var cut = RenderRoom();

        await DeleteAsync(cut, handler);
        await Task.Delay(300);

        AssertSnackbars((notSupported, Severity.Warning));
        Assert.Contains("Alpha Lounge", cut.Markup);
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(StatusCalls);
    }

    // ---- Following the deletion --------------------------------------------------------------------

    [Fact]
    public async Task ActiveStatus_ShowsTheDeletionAsRunning()
    {
        _status = _ => Task.FromResult(Status(RoomDeletionState.Active));
        var cut = RenderRoom();

        await DeleteAsync(cut);

        cut.WaitForAssertion(() => Assert.Contains(ActiveText, Markup(cut)));
        Assert.DoesNotContain(ScheduledText, Markup(cut));
        AssertSnackbars((Requested, Severity.Success));
    }

    [Fact]
    public async Task CompleteStatus_ReportsTheDeletion_AndShowsTheRoomAsDeleted()
    {
        _status = _ => Task.FromResult(Status(RoomDeletionState.Complete));
        var cut = RenderRoom();

        await DeleteAsync(cut);

        cut.WaitForAssertion(() => Assert.Contains(Localizer["RoomDeleted"].Value, Markup(cut)));
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.DoesNotContain("Alpha Lounge", cut.Markup);
        AssertSnackbars((Requested, Severity.Success), (Localizer["RoomDeletedSuccessfully"].Value, Severity.Success));
        await Task.Delay(300);
        Assert.Single(StatusCalls);
    }

    [Theory]
    [InlineData(RoomDeletionState.Failed, "RoomDeletionFailed", Severity.Error)]
    [InlineData(RoomDeletionState.Unknown, "RoomDeletionStatusUnknown", Severity.Warning)]
    public async Task EndedWithoutDeletion_RemovesTheBanner_ReportsIt_AndKeepsTheRoom(RoomDeletionState state, string key, Severity severity)
    {
        _status = _ => Task.FromResult(Status(state));
        var cut = RenderRoom();

        await DeleteAsync(cut);

        cut.WaitForAssertion(() => Assert.Equal(2, SnackbarCalls.Count));
        AssertSnackbars((Requested, Severity.Success), (Localizer[key].Value, severity));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Banner, Markup(cut)));
        Assert.Contains("Alpha Lounge", cut.Markup);
    }

    [Fact]
    public async Task ThrowingStatusRequest_RemovesTheBanner_LogsIt_AndReportsTheError()
    {
        var logger = new RecordingLogger<RoomDetails>();
        Services.AddSingleton<ILogger<RoomDetails>>(logger);
        var error = new InvalidOperationException("boom");
        _status = _ => Task.FromException<OperationResult<RoomDeletionStatusViewModel>>(error);
        var cut = RenderRoom();

        await DeleteAsync(cut);

        cut.WaitForAssertion(() => Assert.Equal(2, SnackbarCalls.Count));
        AssertSnackbars((Requested, Severity.Success), (Localizer["ErrorFetchingRoomDeletionStatus"].Value, Severity.Error));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Banner, Markup(cut)));
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("Unexpected error in room deletion polling loop for room {RoomId}, delete ID {DeleteId}", entry.Template);
        Assert.Equal(new Dictionary<string, object?> { ["RoomId"] = RoomA, ["DeleteId"] = "delete-1" }, entry.Arguments);
        Assert.Same(error, entry.Exception);
    }

    /// <summary>
    /// Failed status requests are retried every 5 s; the fifth failure in a row removes the banner and reports it, as for
    /// purge. Takes about 20 s, because the poll interval is a constant.
    /// </summary>
    [Fact]
    public async Task FiveFailedStatusRequestsInARow_AreLogged_ThenRemoveTheBannerAndReportTheError()
    {
        var logger = new RecordingLogger<RoomDetails>();
        Services.AddSingleton<ILogger<RoomDetails>>(logger);
        int? snackbarsBeforeFifthRequest = null;
        _status = _ =>
        {
            if (StatusCalls.Count == 5) snackbarsBeforeFifthRequest = SnackbarCalls.Count;
            return Task.FromResult(OperationResult<RoomDeletionStatusViewModel>.Failure("status unavailable"));
        };
        var cut = RenderRoom();

        await DeleteAsync(cut);

        cut.WaitForAssertion(() => Assert.Equal(2, SnackbarCalls.Count), TimeSpan.FromSeconds(30));
        Assert.Equal(1, snackbarsBeforeFifthRequest);
        Assert.Equal(5, StatusCalls.Count);
        AssertSnackbars((Requested, Severity.Success), (Localizer["ErrorFetchingRoomDeletionStatus"].Value, Severity.Error));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Banner, Markup(cut)));

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.All(warnings, w => Assert.Equal(
            "Failed to get room deletion status for room {RoomId}, delete ID {DeleteId}. Attempt {Attempt} of {MaxAttempts}. Error: {Error}",
            w.Template));
        Assert.Equal([1, 2, 3, 4, 5], warnings.Select(w => w.Arguments["Attempt"]));
        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["RoomId"] = RoomA, ["DeleteId"] = "delete-1", ["Attempt"] = 1, ["MaxAttempts"] = 5, ["Error"] = "status unavailable"
            },
            warnings[0].Arguments);
    }

    /// <summary>
    /// If the cleanup after a failed check fails too, for example because the circuit is gone, the polling error is still
    /// logged and the cleanup failure is logged at Debug, as for purge (#535 characterization).
    /// </summary>
    [Fact]
    public async Task FailingCleanupAfterAPollingError_LogsThePollingError_AndTheCleanupFailureAtDebug()
    {
        var logger = new RecordingLogger<RoomDetails>();
        Services.AddSingleton<ILogger<RoomDetails>>(logger);
        var snackbarTried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupFailure = new InvalidOperationException("circuit gone");
        SnackbarStub.On(nameof(ISnackbar.Add), _ =>
        {
            snackbarTried.TrySetResult();
            throw cleanupFailure;
        });
        var pollingError = new InvalidOperationException("boom");
        // Found on load, so no "requested" snackbar runs into the throwing stub first.
        _running = roomId => roomId == RoomA ? RunningDeletion("delete-9", RoomDeletionState.Active) : NoRunningDeletion();
        _status = _ => Task.FromException<OperationResult<RoomDeletionStatusViewModel>>(pollingError);

        RenderRoom();

        await snackbarTried.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("Unexpected error in room deletion polling loop for room {RoomId}, delete ID {DeleteId}", error.Template);
        Assert.Same(pollingError, error.Exception);

        const string CleanupTemplate = "Room deletion polling cleanup failed for room {RoomId} (circuit likely gone)";
        Assert.True(SpinWait.SpinUntil(() => logger.Entries.Any(e => e.Template == CleanupTemplate), TimeSpan.FromSeconds(10)));
        var cleanup = Assert.Single(logger.Entries, e => e.Template == CleanupTemplate);
        Assert.Equal(LogLevel.Debug, cleanup.Level);
        Assert.Equal(new Dictionary<string, object?> { ["RoomId"] = RoomA }, cleanup.Arguments);
        Assert.Same(cleanupFailure, cleanup.Exception);
    }

    // ---- Opening a room with a running deletion ----------------------------------------------------

    [Theory]
    [InlineData(RoomDeletionState.Scheduled)]
    [InlineData(RoomDeletionState.Active)]
    public void RunningDeletion_OnLoad_ShowsTheBanner_AndChecksIt(RoomDeletionState state)
    {
        _running = roomId => roomId == RoomA ? RunningDeletion("delete-9", state) : NoRunningDeletion();
        _status = _ => Task.FromResult(Status(state, "delete-9"));

        var cut = RenderRoom();

        cut.WaitForAssertion(() => Assert.Contains(state == RoomDeletionState.Active ? ActiveText : ScheduledText, Markup(cut)));
        Assert.Contains(Banner, Markup(cut));
        cut.WaitForAssertion(() => Assert.Equal("delete-9", StatusCalls[0][0]));
        Assert.Empty(SnackbarCalls);
    }

    /// <summary>A homeserver without the status endpoint, or a failed lookup, shows no banner and no error (AC-11).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoRunningDeletionOrFailedLookup_OnLoad_ShowsNoBannerAndNoSnackbar(bool lookupFails)
    {
        _running = _ => lookupFails
            ? Task.FromResult(OperationResult<RoomDeletionStatusViewModel?>.Failure("lookup failed"))
            : NoRunningDeletion();

        var cut = RenderRoom();
        await Task.Delay(300);

        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(SnackbarCalls);
        Assert.Empty(StatusCalls);
    }

    /// <summary>A homeserver that refuses the lookup (Tuwunel, #559) opens the room quietly, without its warning.</summary>
    [Fact]
    public async Task NotSupportedLookup_OnLoad_ShowsNoBannerAndNoSnackbar()
    {
        _running = _ => Task.FromResult(OperationResult<RoomDeletionStatusViewModel?>.Failure(
            Localizer["OperationNotSupportedByHomeserver"].Value, Severity.Warning));

        var cut = RenderRoom();
        await Task.Delay(300);

        Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetActiveRoomDeletionAsync)));
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(SnackbarCalls);
        Assert.Empty(StatusCalls);
    }

    // ---- Late answers (AC-5)-----------------------------------------------------------------------

    /// <summary>Navigating away stops the checks: a late "complete" for room A changes nothing on room B.</summary>
    [Fact]
    public async Task NavigationDuringStatusRequest_IgnoresTheLateStatus()
    {
        var pending = Pending<OperationResult<RoomDeletionStatusViewModel>>();
        _status = _ => pending.Task;
        var cut = RenderRoom();
        await DeleteAsync(cut);
        cut.WaitForAssertion(() => Assert.Single(StatusCalls));

        Navigate(cut, RoomB);
        Assert.DoesNotContain(Banner, Markup(cut));
        pending.SetResult(Status(RoomDeletionState.Complete));
        await Task.Delay(500);

        AssertSnackbars((Requested, Severity.Success));
        Assert.Contains("Bravo Lounge", cut.Markup);
        Assert.DoesNotContain(Localizer["RoomDeleted"].Value, Markup(cut));
        Assert.Single(StatusCalls);
    }

    /// <summary>
    /// A check that answers "cancelled" after the page moved on is not a failed attempt: nothing is logged or reported
    /// (#535 characterization).
    /// </summary>
    [Fact]
    public async Task CancelledStatusAfterNavigation_LogsAndReportsNothing()
    {
        var logger = new RecordingLogger<RoomDetails>();
        Services.AddSingleton<ILogger<RoomDetails>>(logger);
        var pending = Pending<OperationResult<RoomDeletionStatusViewModel>>();
        _running = roomId => roomId == RoomA ? RunningDeletion("delete-9", RoomDeletionState.Active) : NoRunningDeletion();
        _status = _ => pending.Task;
        var cut = RenderRoom();
        cut.WaitForAssertion(() => Assert.Single(StatusCalls));

        Navigate(cut, RoomB);
        pending.SetResult(OperationResult<RoomDeletionStatusViewModel>.Cancelled());
        await Task.Delay(500);

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Empty(SnackbarCalls);
        Assert.Single(StatusCalls);
    }

    [Fact]
    public async Task LateLookupForThePreviousRoom_ShowsNoBannerOnTheCurrentRoom()
    {
        var lookupA = Pending<OperationResult<RoomDeletionStatusViewModel?>>();
        _running = roomId => roomId == RoomA ? lookupA.Task : NoRunningDeletion();
        var cut = RenderRoom();

        Navigate(cut, RoomB);
        lookupA.SetResult(OperationResult<RoomDeletionStatusViewModel?>.Ok(
            new RoomDeletionStatusViewModel { DeleteId = "delete-a", RoomId = RoomA, State = RoomDeletionState.Active }));
        await Task.Delay(500);

        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(StatusCalls);
    }

    /// <summary>A new deletion replaces the one shown: a late "failed" for the older one changes nothing.</summary>
    [Fact]
    public async Task LateStatusForAnOlderDeletion_KeepsTheNewerDeletion()
    {
        var olderStatus = Pending<OperationResult<RoomDeletionStatusViewModel>>();
        _running = roomId => roomId == RoomA ? RunningDeletion("delete-old", RoomDeletionState.Active) : NoRunningDeletion();
        _status = deleteId => deleteId == "delete-old" ? olderStatus.Task : Pending<OperationResult<RoomDeletionStatusViewModel>>().Task;
        var cut = RenderRoom();
        cut.WaitForAssertion(() => Assert.Contains(StatusCalls, c => (string)c[0]! == "delete-old"));

        await DeleteAsync(cut);
        cut.WaitForAssertion(() => Assert.Contains(StatusCalls, c => (string)c[0]! == "delete-1"));
        olderStatus.SetResult(Status(RoomDeletionState.Failed, "delete-old"));
        await Task.Delay(500);

        AssertSnackbars((Requested, Severity.Success));
        Assert.Equal("delete-1", GetField<string>(cut.Instance, "activeDeleteId"));
        Assert.Contains(Banner, Markup(cut));
    }

    // ---- Room actions during a deletion or a purge ------------------------------------------------

    // The block state is unknown here (the stub answers no block status), so both Block and Unblock are shown (#586).
    private static readonly string[] ActionKeys =
        ["DeleteRoom", "DeleteAndBlockRoom", "BlockRoom", "UnblockRoom", "QuarantineMedia", "PurgeHistory", "MakeRoomAdmin", "JoinUser"];

    /// <summary>Whether each of the eight Actions buttons is disabled, keyed by its resource key.</summary>
    private Dictionary<string, bool> ActionsDisabled(IRenderedComponent<RoomDetails> cut)
    {
        var buttons = cut.FindAll("button").ToList();
        return ActionKeys.ToDictionary(key => key, key =>
            Assert.Single(buttons, b => WebUtility.HtmlDecode(b.TextContent).Trim() == Localizer[key].Value).HasAttribute("disabled"));
    }

    private void AssertActions(IRenderedComponent<RoomDetails> cut, bool disabled) =>
        cut.WaitForAssertion(() => Assert.Equal(ActionKeys.ToDictionary(k => k, _ => disabled), ActionsDisabled(cut)));

    [Fact]
    public void Actions_AreEnabled_WithoutARunningOperation()
    {
        var cut = RenderRoom();

        AssertActions(cut, disabled: false);
    }

    /// <summary>Every way a deletion can end without deleting the room enables the actions again.</summary>
    [Theory]
    [InlineData("failed")]
    [InlineData("unknown")]
    [InlineData("throws")]
    public async Task Actions_AreDisabledWhileADeletionRuns_AndEnabledAgainWhenItEnds(string ending)
    {
        var status = Pending<OperationResult<RoomDeletionStatusViewModel>>();
        _status = _ => status.Task;
        var cut = RenderRoom();

        await DeleteAsync(cut);
        AssertActions(cut, disabled: true);

        switch (ending)
        {
            case "failed": status.SetResult(Status(RoomDeletionState.Failed)); break;
            case "unknown": status.SetResult(Status(RoomDeletionState.Unknown)); break;
            default: status.SetException(new InvalidOperationException("boom")); break;
        }
        AssertActions(cut, disabled: false);
    }

    [Fact]
    public void Actions_AreDisabled_WhileADeletionFoundOnLoadRuns()
    {
        _running = roomId => roomId == RoomA ? RunningDeletion("delete-9", RoomDeletionState.Scheduled) : NoRunningDeletion();

        var cut = RenderRoom();

        AssertActions(cut, disabled: true);
    }

    /// <summary>A tracked purge disables the actions too, until it ends (complete, failed, or giving up after an error).</summary>
    [Theory]
    [InlineData("complete")]
    [InlineData("failed")]
    [InlineData("throws")]
    public void Actions_AreDisabledWhileAPurgeRuns_AndEnabledAgainWhenItEnds(string ending)
    {
        var status = Pending<OperationResult<SynapseAdminPurgeHistoryStatusResponse>>();
        _rooms
            .On(nameof(IRoomService.GetActivePurgeId), args => (string)args[0]! == RoomA ? "purge-a" : null)
            .On(nameof(IRoomService.GetPurgeHistoryStatusAsync), _ => status.Task);
        var cut = RenderRoom();

        AssertActions(cut, disabled: true);

        if (ending == "throws") status.SetException(new InvalidOperationException("boom"));
        else status.SetResult(OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Ok(new SynapseAdminPurgeHistoryStatusResponse { Status = ending }));
        AssertActions(cut, disabled: false);
    }
}
