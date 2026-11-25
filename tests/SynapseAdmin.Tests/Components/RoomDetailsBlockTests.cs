using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The room's block state on the room page (#586, AC-18): what the Overview shows, which of Block and Unblock the
/// Actions card offers, the confirmed actions and the reads of the state that follow them, and late answers.
/// </summary>
public class RoomDetailsBlockTests : DetailPageTestBase
{
    private const string RoomA = "!a:hs";
    private const string RoomB = "!b:hs";

    private readonly Stub _rooms;
    private Func<string, Task<OperationResult<RoomBlockStatusViewModel>>> _blockStatus = _ => Task.FromResult(NotBlocked());
    private OperationResult _blockResult = OperationResult.Ok("Done");

    public RoomDetailsBlockTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), args => Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(
                new RoomDetailViewModel { RoomId = (string)args[0]!, Name = (string)args[0]! == RoomA ? "Alpha Lounge" : "Bravo Lounge" })))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ =>
                Task.FromResult(OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel())))
            .On(nameof(IRoomService.GetRoomBlockStatusAsync), args => _blockStatus((string)args[0]!))
            .On(nameof(IRoomService.BlockRoomAsync), _ => Task.FromResult(_blockResult))
            .On(nameof(IRoomService.DeleteRoomAsync), _ => Task.FromResult(OperationResult<string>.Ok("delete-1")))
            .On(nameof(IRoomService.GetRoomDeletionStatusAsync), _ => Task.FromResult(OperationResult<RoomDeletionStatusViewModel>.Ok(
                new RoomDeletionStatusViewModel { DeleteId = "delete-1", RoomId = RoomA, State = RoomDeletionState.Failed })));
    }

    private IStringLocalizer<SharedResources> Localizer => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static OperationResult<RoomBlockStatusViewModel> NotBlocked() =>
        OperationResult<RoomBlockStatusViewModel>.Ok(new RoomBlockStatusViewModel { IsBlocked = false });

    private static OperationResult<RoomBlockStatusViewModel> Blocked(string blockedBy = "@admin:hs") =>
        OperationResult<RoomBlockStatusViewModel>.Ok(new RoomBlockStatusViewModel { IsBlocked = true, BlockedBy = blockedBy });

    private int BlockStatusReads => _rooms.CallsTo(nameof(IRoomService.GetRoomBlockStatusAsync)).Count;

    private IRenderedComponent<RoomDetails> RenderRoom(string roomId = RoomA)
    {
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, roomId));
        cut.WaitForAssertion(() => Assert.Contains(roomId == RoomA ? "Alpha Lounge" : "Bravo Lounge", cut.Markup));
        return cut;
    }

    /// <summary>
    /// The Overview's block entry: its label, its one chip and the blocker, if shown. The chip renders a <c>div</c>,
    /// which closes the list item's <c>p</c>, so everything is found from the list item (AGENTS.md, bUnit pitfalls).
    /// </summary>
    private static (string Label, string Chip, string? Blocker) BlockEntry(IRenderedComponent<RoomDetails> cut)
    {
        var item = cut.Find(".room-block-status").Closest(".mud-list-item")!;
        static string Text(AngleSharp.Dom.IElement e) => WebUtility.HtmlDecode(e.TextContent).Trim();
        return (Text(item.QuerySelector("b")!), Text(Assert.Single(item.QuerySelectorAll(".mud-chip"))),
            item.QuerySelector(".room-block-blocker") is { } blocker ? Text(blocker) : null);
    }

    private (string, string, string?) Entry(string chipKey, string? blocker = null) =>
        ($"{Localizer["BlockState"].Value}:", Localizer[chipKey].Value, blocker == null ? null : Localizer["BlockedBy", blocker].Value);

    private string[] BlockButtons(IRenderedComponent<RoomDetails> cut)
    {
        string[] labels = [Localizer["BlockRoom"].Value, Localizer["UnblockRoom"].Value];
        return cut.FindAll("button").Select(b => WebUtility.HtmlDecode(b.TextContent).Trim()).Where(labels.Contains).ToArray();
    }

    /// <summary>
    /// Answers the handler's confirmation and renders afterwards, as a click does: <see cref="DetailPageTestBase.InvokeHandler"/>
    /// doesn't render, and the page's own load may not render again in time.
    /// </summary>
    private async Task ConfirmAsync(IRenderedComponent<RoomDetails> cut, string handler, bool? answer = true)
    {
        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(answer);
        await action;
        cut.Render(p => p.Add(x => x.RoomId, RoomA));
    }

    [Fact]
    public void BlockedRoom_ShowsTheBlocker_AndOffersOnlyUnblock()
    {
        _blockStatus = _ => Task.FromResult(Blocked("@mod:hs"));
        var cut = RenderRoom();

        cut.WaitForAssertion(() => Assert.Equal(Entry("Blocked", "@mod:hs"), BlockEntry(cut)));
        Assert.Equal([Localizer["UnblockRoom"].Value], BlockButtons(cut));
        Assert.Equal(RoomA, Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetRoomBlockStatusAsync)))[0]);
    }

    [Fact]
    public void BlockedRoomWithoutABlocker_ShowsOnlyTheChip()
    {
        _blockStatus = _ => Task.FromResult(Blocked(blockedBy: ""));
        var cut = RenderRoom();

        cut.WaitForAssertion(() => Assert.Equal(Entry("Blocked"), BlockEntry(cut)));
    }

    [Fact]
    public void UnblockedRoom_SaysSo_AndOffersOnlyBlock()
    {
        var cut = RenderRoom();

        cut.WaitForAssertion(() => Assert.Equal(Entry("NotBlocked"), BlockEntry(cut)));
        Assert.Equal([Localizer["BlockRoom"].Value], BlockButtons(cut));
    }

    /// <summary>A state that can't be read is unknown: the page reports a failure and offers both actions.</summary>
    [Theory]
    [InlineData("failure", 1)]
    [InlineData("cancelled", 0)]
    public void UnreadableState_IsUnknown_AndOffersBothActions(string kind, int snackbars)
    {
        var result = Make<OperationResult>(kind);
        _blockStatus = _ => Task.FromResult(WithData<RoomBlockStatusViewModel>(result));
        var cut = RenderRoom();

        cut.WaitForAssertion(() => Assert.Equal(1, BlockStatusReads));
        cut.WaitForAssertion(() => Assert.Equal(Entry("BlockStatusUnknown"), BlockEntry(cut)));
        Assert.Equal([Localizer["BlockRoom"].Value, Localizer["UnblockRoom"].Value], BlockButtons(cut));
        Assert.Equal(snackbars, SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Count);
    }

    [Theory]
    [InlineData("BlockRoom", true, "BlockRoomTitle", "BlockRoomConfirmation", "Block")]
    [InlineData("UnblockRoom", false, "UnblockRoomTitle", "UnblockRoomConfirmation", "Unblock")]
    public async Task ConfirmedAction_NamesTheRoom_SendsTheFlag_AndReadsTheStateAgain(
        string handler, bool block, string titleKey, string confirmKey, string yesKey)
    {
        _blockStatus = _ => Task.FromResult(block ? NotBlocked() : Blocked());
        var cut = RenderRoom();
        cut.WaitForAssertion(() => Assert.Equal(1, BlockStatusReads));
        _blockStatus = _ => Task.FromResult(block ? Blocked() : NotBlocked());

        await ConfirmAsync(cut, handler);

        var dialog = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowMessageBoxAsync)));
        Assert.Equal(
            (Localizer[titleKey].Value, Localizer[confirmKey, $"Alpha Lounge ({RoomA})"].Value, Localizer[yesKey].Value, Localizer["Cancel"].Value),
            (dialog[0], dialog[1], dialog[2], dialog[4]));
        var call = Assert.Single(_rooms.CallsTo(nameof(IRoomService.BlockRoomAsync)));
        Assert.Equal((RoomA, block), ((string)call[0]!, (bool)call[1]!));
        AssertSnackbars(_blockResult);
        Assert.Equal(2, BlockStatusReads);
        cut.WaitForAssertion(() => Assert.Equal([Localizer[block ? "UnblockRoom" : "BlockRoom"].Value], BlockButtons(cut)));
    }

    [Theory]
    [InlineData("BlockRoom")]
    [InlineData("UnblockRoom")]
    public async Task FailedAction_IsReported_AndDoesNotReadTheStateAgain(string handler)
    {
        _blockResult = OperationResult.Failure("Block failed");
        var cut = RenderRoom();
        cut.WaitForAssertion(() => Assert.Equal(1, BlockStatusReads));

        await ConfirmAsync(cut, handler);

        AssertSnackbars(_blockResult);
        Assert.Equal(1, BlockStatusReads);
    }

    [Theory]
    [InlineData("BlockRoom", false)]
    [InlineData("BlockRoom", null)]
    [InlineData("UnblockRoom", false)]
    public async Task DeclinedConfirmation_SendsNothing(string handler, bool? answer)
    {
        var cut = RenderRoom();

        await ConfirmAsync(cut, handler, answer);

        Assert.Empty(_rooms.CallsTo(nameof(IRoomService.BlockRoomAsync)));
        Assert.Empty(SnackbarStub.CallsTo(nameof(ISnackbar.Add)));
    }

    [Fact]
    public void LateStateForThePreviousRoom_IsDiscarded()
    {
        var lateA = Pending<OperationResult<RoomBlockStatusViewModel>>();
        _blockStatus = roomId => roomId == RoomA ? lateA.Task : Task.FromResult(NotBlocked());
        var cut = RenderRoom(RoomA);
        cut.WaitForAssertion(() => Assert.Equal(1, BlockStatusReads));

        cut.Render(p => p.Add(x => x.RoomId, RoomB));
        cut.WaitForAssertion(() => Assert.Contains("Bravo Lounge", cut.Markup));
        cut.WaitForAssertion(() => Assert.Equal(Entry("NotBlocked"), BlockEntry(cut)));

        lateA.SetResult(Blocked());

        cut.WaitForAssertion(() => Assert.Equal(Entry("NotBlocked"), BlockEntry(cut)));
        Assert.Equal([Localizer["BlockRoom"].Value], BlockButtons(cut));
    }

    /// <summary>
    /// An action clicked before the first read returned reads the state again; the first read's late answer doesn't
    /// overwrite that newer state.
    /// </summary>
    [Fact]
    public async Task SlowFirstRead_DoesNotOverwriteTheStateReadAfterAnAction()
    {
        var first = Pending<OperationResult<RoomBlockStatusViewModel>>();
        _blockStatus = _ => first.Task;
        var cut = RenderRoom();
        cut.WaitForAssertion(() => Assert.Equal(1, BlockStatusReads));
        _blockStatus = _ => Task.FromResult(Blocked());

        await ConfirmAsync(cut, "BlockRoom");
        cut.WaitForAssertion(() => Assert.Equal([Localizer["UnblockRoom"].Value], BlockButtons(cut)));

        first.SetResult(NotBlocked());

        // The page loads the messages right after it has handled the first read's answer.
        cut.WaitForAssertion(() => Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetRoomMessagesAsync))));
        Assert.Equal([Localizer["UnblockRoom"].Value], BlockButtons(cut));
        Assert.Equal(Entry("Blocked", "@admin:hs"), BlockEntry(cut));
    }

    /// <summary>A deletion that fails may still have blocked the room (Delete &amp; Block), so the page reads the state again.</summary>
    [Fact]
    public async Task DeletionThatEndsWithoutDeletingTheRoom_ReadsTheStateAgain()
    {
        var cut = RenderRoom();
        cut.WaitForAssertion(() => Assert.Equal(1, BlockStatusReads));
        _blockStatus = _ => Task.FromResult(Blocked());

        await ConfirmAsync(cut, "DeleteAndBlockRoom");

        cut.WaitForAssertion(() => Assert.Equal(2, BlockStatusReads));
        cut.WaitForAssertion(() => Assert.Equal([Localizer["UnblockRoom"].Value], BlockButtons(cut)));
    }
}
