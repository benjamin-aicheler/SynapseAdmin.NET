using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
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
            .On(nameof(IRoomService.DeleteRoomAsync), _ => Task.FromResult(OperationResult.Ok()))
            .On(nameof(IRoomService.QuarantineMediaAsync), _ => Task.FromResult(OperationResult.Ok()))
            .On(nameof(IRoomService.BlockRoomAsync), _ => Task.FromResult(OperationResult.Ok()));
    }

    private static OperationResult<RoomDetailViewModel> Found(string roomId) =>
        OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel { RoomId = roomId, Name = roomId == RoomA ? "Alpha Lounge" : "Bravo Lounge" });

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
}
