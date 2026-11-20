using Bunit;
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
/// Issue #367: destructive room actions must target the room shown in the UI,
/// never a newer route parameter, and stale room data must never be shown.
/// </summary>
public class RoomDetailsStaleSelectionTests : DetailPageTestBase
{
    private const string RoomA = "!a:hs";
    private const string RoomB = "!b:hs";

    private readonly Stub _rooms;
    private readonly Dictionary<string, TaskCompletionSource<OperationResult<RoomDetailViewModel>>> _pendingDetails = new();
    private readonly Dictionary<string, CancellationToken> _detailTokens = new();
    private TaskCompletionSource<OperationResult<List<RoomMediaItemViewModel>>>? _pendingMediaBatch;

    public RoomDetailsStaleSelectionTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), args =>
            {
                var roomId = (string)args[0]!;
                _detailTokens[roomId] = (CancellationToken)args[1]!;
                if (_pendingDetails.TryGetValue(roomId, out var pending)) return pending.Task;
                return Task.FromResult(Found(roomId));
            })
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ =>
                Task.FromResult(OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel())))
            .On(nameof(IRoomService.GetMediaMetadataBatchAsync), args =>
                _pendingMediaBatch?.Task ?? Task.FromResult(OperationResult<List<RoomMediaItemViewModel>>.Ok(
                    ((List<string>)args[0]!).Select(m => new RoomMediaItemViewModel { MediaId = m }).ToList())))
            .On(nameof(IRoomService.DeleteRoomAsync), _ => Task.FromResult(OperationResult.Ok()))
            .On(nameof(IRoomService.QuarantineMediaAsync), _ => Task.FromResult(OperationResult.Ok()))
            .On(nameof(IRoomService.BlockRoomAsync), _ => Task.FromResult(OperationResult.Ok()));
    }

    private static OperationResult<RoomDetailViewModel> Found(string roomId) =>
        OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel
        {
            RoomId = roomId,
            Name = roomId == RoomA ? "Alpha Lounge" : "Bravo Lounge",
            Media = new RoomMediaViewModel
            {
                Local = [new RoomMediaItemViewModel { MediaId = $"mxc://hs/local-{roomId[1]}" }],
                Remote = [new RoomMediaItemViewModel { MediaId = $"mxc://remote/remote-{roomId[1]}" }]
            }
        });

    private static OperationResult<RoomDetailViewModel> Failed() =>
        OperationResult<RoomDetailViewModel>.Failure("Room could not be loaded");

    private IRenderedComponent<RoomDetails> RenderRoom(string roomId) =>
        Render<RoomDetails>(p => p.Add(x => x.RoomId, roomId));

    private static void Navigate(IRenderedComponent<RoomDetails> cut, string roomId) =>
        cut.Render(p => p.Add(x => x.RoomId, roomId));

    private IReadOnlyList<object?[]> DeleteCalls => _rooms.CallsTo(nameof(IRoomService.DeleteRoomAsync));

    [Fact]
    public async Task FailedLoadAfterNavigation_DoesNotShowPreviousRoomOrAllowActions()
    {
        var cut = RenderRoom(RoomA);
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));

        _pendingDetails[RoomB] = Pending<OperationResult<RoomDetailViewModel>>();
        Navigate(cut, RoomB);

        // While B is loading, A must already be gone.
        cut.WaitForAssertion(() => Assert.DoesNotContain("Alpha Lounge", cut.Markup));

        _pendingDetails[RoomB].SetResult(Failed());
        cut.WaitForAssertion(() => Assert.Contains("Room could not be loaded", cut.Markup));
        Assert.DoesNotContain("Alpha Lounge", cut.Markup);
        Assert.Null(GetField<RoomDetailViewModel>(cut.Instance, "room"));

        // Even if a handler is triggered, nothing is deleted and no confirmation is shown.
        await InvokeHandler(cut, "DeleteRoom");
        await InvokeHandler(cut, "DeleteAndBlockRoom");
        await InvokeHandler(cut, "QuarantineMedia");
        await InvokeHandler(cut, "BlockRoom");
        Assert.Empty(Confirmations);
        Assert.Empty(DeleteCalls);
        Assert.Empty(_rooms.CallsTo(nameof(IRoomService.QuarantineMediaAsync)));
        Assert.Empty(_rooms.CallsTo(nameof(IRoomService.BlockRoomAsync)));
    }

    [Fact]
    public async Task ReversedResponseOrder_OlderResponseDoesNotOverwriteNewerRoom()
    {
        _pendingDetails[RoomA] = Pending<OperationResult<RoomDetailViewModel>>();
        _pendingDetails[RoomB] = Pending<OperationResult<RoomDetailViewModel>>();

        var cut = RenderRoom(RoomA);
        Navigate(cut, RoomB);

        // The request for A is cancelled as soon as B is selected.
        cut.WaitForAssertion(() => Assert.True(_detailTokens.ContainsKey(RoomB)));
        Assert.True(_detailTokens[RoomA].IsCancellationRequested);
        Assert.False(_detailTokens[RoomB].IsCancellationRequested);

        _pendingDetails[RoomB].SetResult(Found(RoomB));
        cut.WaitForAssertion(() => Assert.Contains("Bravo Lounge", cut.Markup));

        // A's response arrives late (server ignored cancellation) and must be discarded.
        _pendingDetails[RoomA].SetResult(Found(RoomA));
        cut.WaitForAssertion(() => Assert.Contains("Bravo Lounge", cut.Markup));
        Assert.DoesNotContain("Alpha Lounge", cut.Markup);
        Assert.Equal(RoomB, GetField<RoomDetailViewModel>(cut.Instance, "room")!.RoomId);

        var action = InvokeHandler(cut, "DeleteRoom");
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Assert.Contains(RoomB, Confirmations[0].Message);
        Confirmations[0].Result.SetResult(true);
        await action;

        Assert.Equal(RoomB, Assert.Single(DeleteCalls)[0]);
    }

    [Theory]
    [InlineData("DeleteRoom")]
    [InlineData("DeleteAndBlockRoom")]
    [InlineData("QuarantineMedia")]
    public async Task NavigationDuringConfirmation_AbortsActionInsteadOfRetargeting(string handler)
    {
        var cut = RenderRoom(RoomA);
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));

        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Assert.Contains("Alpha Lounge", Confirmations[0].Message);
        Assert.Contains(RoomA, Confirmations[0].Message);

        Navigate(cut, RoomB);
        cut.WaitForAssertion(() => Assert.Contains("Bravo Lounge", cut.Markup));

        Confirmations[0].Result.SetResult(true);
        await action;

        Assert.Empty(DeleteCalls);
        Assert.Empty(_rooms.CallsTo(nameof(IRoomService.QuarantineMediaAsync)));
        Assert.True(WarningShown());
    }

    [Theory]
    [InlineData("DeleteRoom", false)]
    [InlineData("DeleteAndBlockRoom", true)]
    public async Task ConfirmedDelete_TargetsDisplayedRoom(string handler, bool expectedBlock)
    {
        var cut = RenderRoom(RoomA);
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));

        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;

        var call = Assert.Single(DeleteCalls);
        Assert.Equal(RoomA, call[0]);
        Assert.Equal(expectedBlock, call[1]);
        Assert.Equal(true, call[2]);
        Assert.False(WarningShown());
    }

    private static Task<TableData<RoomMediaItemViewModel>> InvokeMediaLoader(
        IRenderedComponent<RoomDetails> cut, string name, CancellationToken token = default)
    {
        var method = typeof(RoomDetails).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var state = new TableState { Page = 0, PageSize = 10 };
        return cut.InvokeAsync(() => (Task<TableData<RoomMediaItemViewModel>>)method.Invoke(cut.Instance, [state, token])!);
    }

    public static TheoryData<string, bool, bool> PendingMediaCases => new()
    {
        // loader, B fails to load (vs. still loading), batch result is a failure (vs. success)
        { "LoadLocalMedia", false, false },
        { "LoadLocalMedia", true, false },
        { "LoadLocalMedia", true, true },
        { "LoadRemoteMedia", false, false },
        { "LoadRemoteMedia", true, false },
        { "LoadRemoteMedia", true, true },
    };

    [Theory]
    [MemberData(nameof(PendingMediaCases))]
    public async Task PendingMediaRequestDuringNavigation_IsDiscardedWithoutThrowing(string loader, bool roomBFails, bool batchFails)
    {
        var cut = RenderRoom(RoomA);
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));

        _pendingMediaBatch = Pending<OperationResult<List<RoomMediaItemViewModel>>>();
        var mediaLoad = InvokeMediaLoader(cut, loader);

        _pendingDetails[RoomB] = Pending<OperationResult<RoomDetailViewModel>>();
        Navigate(cut, RoomB);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Alpha Lounge", cut.Markup));
        if (roomBFails)
        {
            _pendingDetails[RoomB].SetResult(Failed());
            cut.WaitForAssertion(() => Assert.Contains("Room could not be loaded", cut.Markup));
        }
        var snackbarCallsBefore = SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Count;

        // A's media response arrives after `room` was cleared; it must be discarded, not dereference the null model.
        _pendingMediaBatch.SetResult(batchFails
            ? OperationResult<List<RoomMediaItemViewModel>>.Failure("media failed")
            : OperationResult<List<RoomMediaItemViewModel>>.Ok([new RoomMediaItemViewModel { MediaId = "mxc://hs/local-a" }]));
        var data = await mediaLoad;

        Assert.Empty(data.Items!);
        Assert.Equal(0, data.TotalItems);
        Assert.Equal(snackbarCallsBefore, SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Count);
    }

    [Theory]
    [InlineData("LoadLocalMedia", "mxc://hs/local-a")]
    [InlineData("LoadRemoteMedia", "mxc://remote/remote-a")]
    public async Task MediaLoader_ReturnsDisplayedRoomMedia(string loader, string expectedMediaId)
    {
        var cut = RenderRoom(RoomA);
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));

        var data = await InvokeMediaLoader(cut, loader);

        Assert.Equal(1, data.TotalItems);
        Assert.Equal(expectedMediaId, Assert.Single(data.Items!).MediaId);
    }

    // ---- Purge status polling --------------------------------------------------------------------

    private IStringLocalizer<SharedResources> Localizer => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IReadOnlyList<object?[]> StatusCalls => _rooms.CallsTo(nameof(IRoomService.GetPurgeHistoryStatusAsync));
    private IReadOnlyList<object?[]> ClearCalls => _rooms.CallsTo(nameof(IRoomService.ClearActivePurgeId));
    private IReadOnlyList<object?[]> SnackbarCalls => SnackbarStub.CallsTo(nameof(ISnackbar.Add));

    private static OperationResult<SynapseAdminPurgeHistoryStatusResponse> PurgeStatus(string status, string? error = null) =>
        OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Ok(new SynapseAdminPurgeHistoryStatusResponse { Status = status, Error = error });

    /// <summary>Room A has a tracked purge; polling starts once the room is loaded and asks <paramref name="status"/>.</summary>
    private IRenderedComponent<RoomDetails> RenderPurgingRoom(Func<Task<OperationResult<SynapseAdminPurgeHistoryStatusResponse>>> status)
    {
        _rooms
            .On(nameof(IRoomService.GetActivePurgeId), args => (string)args[0]! == RoomA ? "purge-a" : null)
            .On(nameof(IRoomService.GetPurgeHistoryStatusAsync), _ => status());
        var cut = RenderRoom(RoomA);
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));
        return cut;
    }

    private void AssertPurgeClearedAndReported(IRenderedComponent<RoomDetails> cut, string message, Severity severity)
    {
        cut.WaitForAssertion(() => Assert.NotEmpty(SnackbarCalls));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Localizer["PurgeInProgress"].Value, cut.Markup));
        Assert.Equal(RoomA, Assert.Single(ClearCalls)[0]);
        var snackbar = Assert.Single(SnackbarCalls);
        Assert.Equal(message, snackbar[0]);
        Assert.Equal(severity, snackbar[1]);
    }

    [Fact]
    public void CompletePurgeStatus_ClearsThePurge_AndReportsSuccess()
    {
        var cut = RenderPurgingRoom(() => Task.FromResult(PurgeStatus("complete")));

        AssertPurgeClearedAndReported(cut, Localizer["PurgeCompleted"].Value, Severity.Success);
        Assert.Equal("purge-a", Assert.Single(StatusCalls)[0]);
    }

    /// <summary>A failed purge is reported with its error text, or "Unknown error" if it has none.</summary>
    [Theory]
    [InlineData("disk full", "disk full")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void FailedPurgeStatus_ClearsThePurge_AndReportsTheError(string? error, string? shownError)
    {
        var cut = RenderPurgingRoom(() => Task.FromResult(PurgeStatus("failed", error)));

        var message = string.Format(Localizer["PurgeFailed"].Value, shownError ?? Localizer["UnknownError"].Value);
        AssertPurgeClearedAndReported(cut, message, Severity.Error);
    }

    [Fact]
    public void ActivePurgeStatus_KeepsThePurge_AndShowsItAsRunning()
    {
        var cut = RenderPurgingRoom(() => Task.FromResult(PurgeStatus("active")));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".mud-alert .mud-progress-circular")));
        Assert.Empty(ClearCalls);
        Assert.Empty(SnackbarCalls);
    }

    [Fact]
    public void ThrowingStatusRequest_ClearsThePurge_AndReportsTheError()
    {
        var cut = RenderPurgingRoom(() =>
            Task.FromException<OperationResult<SynapseAdminPurgeHistoryStatusResponse>>(new InvalidOperationException("boom")));

        AssertPurgeClearedAndReported(cut, Localizer["ErrorFetchingPurgeStatus"].Value, Severity.Error);
    }

    /// <summary>
    /// If the error cleanup after a failed status request fails too, for example because the circuit is gone, the
    /// polling error is still logged and the cleanup failure doesn't escape. Since O-01 the cleanup failure is logged
    /// at Debug.
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

        RenderPurgingRoom(() => Task.FromException<OperationResult<SynapseAdminPurgeHistoryStatusResponse>>(pollingError));

        await snackbarTried.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RoomA, Assert.Single(ClearCalls)[0]);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("Unexpected error in purge polling loop for room {RoomId}, purge ID {PurgeId}", error.Template);
        Assert.Same(pollingError, error.Exception);

        // The cleanup failure is logged after the snackbar call throws; polling runs in the background.
        const string CleanupTemplate = "Purge polling cleanup failed for room {RoomId} (circuit likely gone)";
        Assert.True(SpinWait.SpinUntil(() => logger.Entries.Any(e => e.Template == CleanupTemplate), TimeSpan.FromSeconds(10)));
        var cleanup = Assert.Single(logger.Entries, e => e.Template == CleanupTemplate);
        Assert.Equal(LogLevel.Debug, cleanup.Level);
        Assert.Equal(new Dictionary<string, object?> { ["RoomId"] = RoomA }, cleanup.Arguments);
        Assert.Same(cleanupFailure, cleanup.Exception);
    }

    /// <summary>Navigating away stops polling: a late "complete" for room A's purge changes nothing.</summary>
    [Fact]
    public async Task NavigationDuringStatusRequest_IgnoresTheLateStatus()
    {
        var requested = Pending<bool>();
        var pending = Pending<OperationResult<SynapseAdminPurgeHistoryStatusResponse>>();
        var cut = RenderPurgingRoom(() =>
        {
            requested.TrySetResult(true);
            return pending.Task;
        });
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(Localizer["PurgeInProgress"].Value, cut.Markup);

        Navigate(cut, RoomB);
        cut.WaitForAssertion(() => Assert.Contains("Bravo Lounge", cut.Markup));
        pending.SetResult(PurgeStatus("complete"));
        await Task.Delay(500);

        Assert.Empty(ClearCalls);
        Assert.Empty(SnackbarCalls);
        Assert.Single(StatusCalls);
    }

    /// <summary>
    /// Failed status requests are retried every 5 s; the fifth failure in a row clears the purge and reports it. Takes
    /// about 20 s, because the poll interval is a constant.
    /// </summary>
    [Fact]
    public void FiveFailedStatusRequestsInARow_ClearThePurge_AndReportTheError()
    {
        (int Clears, int Snackbars)? beforeFifthRequest = null;
        var cut = RenderPurgingRoom(() =>
        {
            if (StatusCalls.Count == 5) beforeFifthRequest = (ClearCalls.Count, SnackbarCalls.Count);
            return Task.FromResult(OperationResult<SynapseAdminPurgeHistoryStatusResponse>.Failure("status unavailable"));
        });

        cut.WaitForAssertion(() => Assert.Single(ClearCalls), TimeSpan.FromSeconds(30));
        Assert.Equal((0, 0), beforeFifthRequest);
        Assert.Equal(5, StatusCalls.Count);
        AssertPurgeClearedAndReported(cut, Localizer["ErrorFetchingPurgeStatus"].Value, Severity.Error);
    }
}
