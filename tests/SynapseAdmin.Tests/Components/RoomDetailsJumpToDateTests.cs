using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
/// Jumping to a date in the room page's messages (#588, AC-33): the local date and time go to the service through
/// <see cref="ITimeZoneService"/>, the list shows both sides with the event found marked, Load more and Load newer page
/// from the jump's tokens without listing a message twice, Refresh leaves the jump, and a failed or late answer changes
/// nothing.
/// </summary>
public class RoomDetailsJumpToDateTests : DetailPageTestBase
{
    private const string RoomA = "!a:hs";
    private const string RoomB = "!b:hs";
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    private readonly Stub _rooms;
    private readonly List<(string RoomId, DateTimeOffset At, TaskCompletionSource<OperationResult<RoomMessagesViewModel>> Result)> _jumps = [];
    private readonly Queue<OperationResult<RoomMessagesViewModel>> _pages = new();

    public RoomDetailsJumpToDateTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), args => Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(
                new RoomDetailViewModel { RoomId = (string)args[0]!, Name = $"Room {args[0]}" })))
            .On(nameof(IRoomService.GetRoomBlockStatusAsync), _ => Task.FromResult(OperationResult<RoomBlockStatusViewModel>.Ok(new RoomBlockStatusViewModel())))
            .On(nameof(IRoomService.GetActiveRoomDeletionAsync), _ => Task.FromResult(OperationResult<RoomDeletionStatusViewModel?>.Ok(null)))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ => Task.FromResult(_pages.Count > 0
                ? _pages.Dequeue()
                : OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel { Messages = [Msg("$newest")] })))
            .On(nameof(IRoomService.GetRoomMessagesAtAsync), args =>
            {
                var jump = Pending<OperationResult<RoomMessagesViewModel>>();
                _jumps.Add(((string)args[0]!, (DateTimeOffset)args[1]!, jump));
                return jump.Task;
            });
        // The local wall-clock time in a fixed zone two hours ahead of UTC, so the test sees the conversion.
        Services.AddSingleton(Stub.Create<ITimeZoneService>(s => s
            .On("get_UserTimeZone", _ => TimeZoneInfo.Utc)
            .On("ToLocal", a => a[0])
            .On("GetLocalNow", _ => DateTimeOffset.UtcNow)
            .On("GetDisplayOffset", _ => "UTC+2")
            .On(nameof(ITimeZoneService.ToDateTimeOffset), a => new DateTimeOffset((DateTime)a[0]!, Offset))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static RoomMessageItemViewModel Msg(string eventId) =>
        new() { EventId = eventId, Sender = "@u:hs", Type = "m.room.message", Body = $"text {eventId}", OriginServerTs = DateTimeOffset.UnixEpoch };

    private static OperationResult<RoomMessagesViewModel> Jumped(string? older = "older", string? newer = "newer", string anchor = "$e", params string[] ids) =>
        OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel
        {
            Messages = (ids.Length == 0 ? ["$a2", "$a1", "$e", "$b1", "$b2"] : ids).Select(Msg).ToList(),
            EndToken = older,
            NewerToken = newer,
            AnchorEventId = anchor,
        });

    private static OperationResult<RoomMessagesViewModel> Page(string? end, params string[] ids) =>
        OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel { Messages = ids.Select(Msg).ToList(), EndToken = end });

    private IRenderedComponent<RoomDetails> RenderRoom(string roomId = RoomA)
    {
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, roomId));
        cut.WaitForAssertion(() => Assert.Equal(["$newest"], ListedIds(cut)));
        return cut;
    }

    /// <summary>The event IDs of the listed message rows, in order, from their forensics links.</summary>
    private static List<string> ListedIds(IRenderedComponent<RoomDetails> cut) =>
        cut.FindAll("tr a[href^='/event-forensics?eventId=']")
            .Select(a => Uri.UnescapeDataString(a.GetAttribute("href")!["/event-forensics?eventId=".Length..]))
            .ToList();

    private AngleSharp.Dom.IElement Button(IRenderedComponent<RoomDetails> cut, string key) =>
        cut.FindAll("button").Single(b => WebUtility.HtmlDecode(b.TextContent).Trim() == L[key].Value);

    private bool HasButton(IRenderedComponent<RoomDetails> cut, string key) =>
        cut.FindAll("button").Any(b => WebUtility.HtmlDecode(b.TextContent).Trim() == L[key].Value);

    private static async Task EnterDateAndTime(IRenderedComponent<RoomDetails> cut, DateTime? date, TimeSpan? time)
    {
        if (date != null) await cut.InvokeAsync(() => cut.FindComponent<MudDatePicker>().Instance.DateChanged.InvokeAsync(date));
        if (time != null) await cut.InvokeAsync(() => cut.FindComponent<MudTimePicker>().Instance.TimeChanged.InvokeAsync(time));
    }

    /// <summary>Enters a date and time, clicks Jump and answers the jump; returns once the page has handled it.</summary>
    private async Task JumpAsync(IRenderedComponent<RoomDetails> cut, OperationResult<RoomMessagesViewModel> answer)
    {
        await EnterDateAndTime(cut, new DateTime(2026, 11, 20), new TimeSpan(14, 30, 0));
        var click = Button(cut, "JumpToDate").ClickAsync(new MouseEventArgs());
        _jumps[^1].Result.SetResult(answer);
        await click.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task JumpButton_IsDisabledUntilDateAndTimeAreSet(bool date, bool time)
    {
        var cut = RenderRoom();

        await EnterDateAndTime(cut, date ? new DateTime(2026, 11, 20) : null, time ? new TimeSpan(14, 30, 0) : null);

        Assert.True(Button(cut, "JumpToDate").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Jump_SendsTheLocalTimeInTheBrowsersZone_AndListsBothSidesWithTheEventMarked()
    {
        var cut = RenderRoom();

        await JumpAsync(cut, Jumped());

        var jump = Assert.Single(_jumps);
        Assert.Equal((RoomA, new DateTimeOffset(2026, 11, 20, 14, 30, 0, Offset)), (jump.RoomId, jump.At));
        Assert.Equal(["$a2", "$a1", "$e", "$b1", "$b2"], ListedIds(cut));
        var anchor = Assert.Single(cut.FindAll("tr.room-message-anchor"));
        Assert.Contains("text $e", anchor.TextContent);
        Assert.Equal(string.Format(L["JumpedToDate"], jump.At.ToString("g")), WebUtility.HtmlDecode(cut.Find(".jumped-to").TextContent).Trim());
        Assert.True(HasButton(cut, "LoadNewer"));
        Assert.True(HasButton(cut, "LoadMore"));
        AssertSnackbars();
    }

    [Fact]
    public async Task Jump_ShowsTheTablePageWithTheEventFound()
    {
        var cut = RenderRoom();
        var ids = Enumerable.Range(0, 25).Select(i => $"$m{i}").ToArray();

        await JumpAsync(cut, Jumped(anchor: "$m12", ids: ids));

        Assert.Single(cut.FindAll("tr.room-message-anchor"));
        Assert.Contains("$m12", ListedIds(cut));
    }

    [Fact]
    public async Task LoadMore_AfterAJump_PagesOlderFromTheJumpsToken_WithoutListingAMessageTwice()
    {
        var cut = RenderRoom();
        await JumpAsync(cut, Jumped());
        _pages.Enqueue(Page("older2", "$b2", "$b3"));

        Button(cut, "LoadMore").Click();

        var call = _rooms.CallsTo(nameof(IRoomService.GetRoomMessagesAsync)).Last();
        Assert.Equal(("older", "b"), ((string?)call[1], (string?)call[3]));
        cut.WaitForAssertion(() => Assert.Equal(["$a2", "$a1", "$e", "$b1", "$b2", "$b3"], ListedIds(cut)));
    }

    [Fact]
    public async Task LoadNewer_PagesNewerFromTheJumpsToken_AndAddsTheMessagesAbove()
    {
        var cut = RenderRoom();
        await JumpAsync(cut, Jumped());
        // A forward page comes oldest first; "$a2" is listed already.
        _pages.Enqueue(Page("newer2", "$a2", "$a3", "$a4"));

        Button(cut, "LoadNewer").Click();

        var call = _rooms.CallsTo(nameof(IRoomService.GetRoomMessagesAsync)).Last();
        Assert.Equal(("newer", "f"), ((string?)call[1], (string?)call[3]));
        cut.WaitForAssertion(() => Assert.Equal(["$a4", "$a3", "$a2", "$a1", "$e"], ListedIds(cut).Take(5)));
        Assert.True(HasButton(cut, "LoadNewer"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("newer")]
    public async Task LoadNewer_WithoutAFurtherToken_HidesTheButton(string? end)
    {
        var cut = RenderRoom();
        await JumpAsync(cut, Jumped());
        _pages.Enqueue(Page(end, "$a3"));

        Button(cut, "LoadNewer").Click();

        cut.WaitForAssertion(() => Assert.False(HasButton(cut, "LoadNewer")));
    }

    [Fact]
    public async Task Jump_WithoutTokens_ShowsNeitherPagingButton()
    {
        var cut = RenderRoom();

        await JumpAsync(cut, Jumped(older: null, newer: null));

        Assert.False(HasButton(cut, "LoadNewer"));
        Assert.False(HasButton(cut, "LoadMore"));
    }

    [Fact]
    public async Task Refresh_LeavesTheJump_AndListsTheNewestMessages()
    {
        var cut = RenderRoom();
        await JumpAsync(cut, Jumped());

        Button(cut, "Refresh").Click();

        cut.WaitForAssertion(() => Assert.Equal(["$newest"], ListedIds(cut)));
        Assert.Null((string?)_rooms.CallsTo(nameof(IRoomService.GetRoomMessagesAsync)).Last()[1]);
        Assert.Empty(cut.FindAll(".jumped-to"));
        Assert.Empty(cut.FindAll("tr.room-message-anchor"));
        Assert.False(HasButton(cut, "LoadNewer"));
    }

    [Theory]
    [InlineData("warning")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public async Task FailedJump_KeepsTheMessagesShown(string kind)
    {
        var result = kind == "warning"
            ? OperationResult<RoomMessagesViewModel>.Failure("No event", Severity.Warning)
            : Make<OperationResult<RoomMessagesViewModel>>(kind);
        var cut = RenderRoom();

        await JumpAsync(cut, result);

        Assert.Equal(["$newest"], ListedIds(cut));
        Assert.Empty(cut.FindAll(".jumped-to"));
        AssertSnackbars(kind == "cancelled" ? [] : [result]);
    }

    [Fact]
    public async Task LateJumpAnswer_ForARoomNoLongerShown_ChangesNothing()
    {
        var cut = RenderRoom(RoomA);
        await EnterDateAndTime(cut, new DateTime(2026, 11, 20), new TimeSpan(14, 30, 0));
        var click = Button(cut, "JumpToDate").ClickAsync(new MouseEventArgs());

        cut.Render(p => p.Add(x => x.RoomId, RoomB));
        cut.WaitForAssertion(() => Assert.Contains($"Room {RoomB}", cut.Markup));
        cut.WaitForAssertion(() => Assert.Equal(["$newest"], ListedIds(cut)));
        _jumps[0].Result.SetResult(Jumped());
        await click.WaitAsync(TimeSpan.FromSeconds(10));
        cut.Render(p => p.Add(x => x.RoomId, RoomB));

        Assert.Equal(["$newest"], ListedIds(cut));
        Assert.Empty(cut.FindAll(".jumped-to"));
        AssertSnackbars();
    }
}
