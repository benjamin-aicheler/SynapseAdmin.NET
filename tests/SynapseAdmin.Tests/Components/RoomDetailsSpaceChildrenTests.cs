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
/// The room page's "Space children" tab (#588, AC-34): only for spaces, listing every child the space names with the
/// details this server has, paging details with <c>next_batch</c>, and showing failures in the tab only.
/// </summary>
public class RoomDetailsSpaceChildrenTests : DetailPageTestBase
{
    private const string Space = "!space:hs";
    private const string OtherSpace = "!space-two:hs";
    private const string PlainRoom = "!plain:hs";

    private readonly Stub _rooms;
    private readonly List<(string RoomId, string? From, CancellationToken Token, TaskCompletionSource<OperationResult<SpaceChildrenPageViewModel>> Result)> _pages = [];

    public RoomDetailsSpaceChildrenTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), args => Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel
            {
                RoomId = (string)args[0]!, Name = $"Room {args[0]}", RoomType = ((string)args[0]!).StartsWith("!space", StringComparison.Ordinal) ? "m.space" : null
            })))
            .On(nameof(IRoomService.GetRoomBlockStatusAsync), _ => Task.FromResult(OperationResult<RoomBlockStatusViewModel>.Ok(new RoomBlockStatusViewModel())))
            .On(nameof(IRoomService.GetActiveRoomDeletionAsync), _ => Task.FromResult(OperationResult<RoomDeletionStatusViewModel?>.Ok(null)))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ => Task.FromResult(OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel())))
            .On(nameof(IRoomService.GetSpaceChildrenAsync), args =>
            {
                var page = Pending<OperationResult<SpaceChildrenPageViewModel>>();
                _pages.Add(((string)args[0]!, (string?)args[1], (CancellationToken)args[2]!, page));
                return page.Task;
            });
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static SpaceChildrenPageViewModel FirstPage(string? next = "next") => new()
    {
        Children = [new() { RoomId = "!c1:hs", Suggested = true }, new() { RoomId = "!c2:hs" }, new() { RoomId = "!remote:other" }],
        Summaries =
        [
            new() { RoomId = "!c1:hs", Name = "One", CanonicalAlias = "#one:hs", JoinedMembers = 4, JoinRule = "public" },
            new() { RoomId = "!c2:hs", Name = "", RoomType = "m.space", JoinRule = "invite" },
        ],
        NextBatch = next,
    };

    private IRenderedComponent<RoomDetails> RenderRoom(string roomId)
    {
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, roomId));
        cut.WaitForAssertion(() => Assert.Contains($"Room {roomId}", cut.Markup));
        return cut;
    }

    /// <summary>Answers a page request and waits until the page has rendered again.</summary>
    private static void Answer(IRenderedComponent<RoomDetails> cut, TaskCompletionSource<OperationResult<SpaceChildrenPageViewModel>> page, OperationResult<SpaceChildrenPageViewModel> result)
    {
        var renders = cut.RenderCount;
        page.SetResult(result);
        cut.WaitForState(() => cut.RenderCount > renders);
    }

    private void OpenTab(IRenderedComponent<RoomDetails> cut) =>
        cut.FindAll(".mud-tab").Single(t => WebUtility.HtmlDecode(t.TextContent).Trim() == L["SpaceChildren"].Value).Click();

    private static List<AngleSharp.Dom.IElement> Rows(IRenderedComponent<RoomDetails> cut) => cut.FindAll(".space-children tbody tr").ToList();

    private static string Cell(AngleSharp.Dom.IElement row, int index) => WebUtility.HtmlDecode(row.Children[index].TextContent).Trim();

    [Fact]
    public void RoomThatIsNoSpace_HasNoTab_AndSendsNoRequest()
    {
        var cut = RenderRoom(PlainRoom);

        Assert.Empty(_pages);
        Assert.DoesNotContain(cut.FindAll(".mud-tab"), t => WebUtility.HtmlDecode(t.TextContent).Trim() == L["SpaceChildren"].Value);
    }

    [Fact]
    public void Space_ListsEveryChildInOrder_WithItsDetailsLinksAndChips()
    {
        var cut = RenderRoom(Space);
        cut.WaitForAssertion(() => Assert.Single(_pages));
        Assert.Equal((Space, (string?)null), (_pages[0].RoomId, _pages[0].From));
        Answer(cut, _pages[0].Result, OperationResult<SpaceChildrenPageViewModel>.Ok(FirstPage()));

        OpenTab(cut);

        var rows = Rows(cut);
        Assert.Equal(["!c1:hs", "!c2:hs", "!remote:other"], rows.Select(r => r.GetAttribute("data-room-id")));
        Assert.Equal(("#one:hs", "4", "public", "-"), (Cell(rows[0], 1), Cell(rows[0], 2), Cell(rows[0], 3), Cell(rows[0], 4)));
        Assert.Equal($"/rooms/{Uri.EscapeDataString("!c1:hs")}", rows[0].QuerySelector("a")?.GetAttribute("href"));
        Assert.Contains("One", Cell(rows[0], 0));
        Assert.NotNull(rows[0].QuerySelector(".space-child-suggested"));
        // A child with details but no name shows its ID as the link text.
        Assert.Equal("!c2:hs", WebUtility.HtmlDecode(rows[1].QuerySelector("a")!.TextContent).Trim());
        Assert.Equal("m.space", Cell(rows[1], 4));
        Assert.Null(rows[1].QuerySelector(".space-child-suggested"));
        // A child this server has no summary of: no link, and the reason.
        Assert.Null(rows[2].QuerySelector("a"));
        Assert.Equal(L["NoDetailsOnThisServer"].Value, WebUtility.HtmlDecode(rows[2].QuerySelector(".space-child-no-details")!.TextContent).Trim());
        Assert.Equal(("-", "-", "-", "-"), (Cell(rows[2], 1), Cell(rows[2], 2), Cell(rows[2], 3), Cell(rows[2], 4)));
        AssertSnackbars();
    }

    [Fact]
    public void LoadMore_SendsTheTokenBack_AndFillsInDetails()
    {
        var cut = RenderRoom(Space);
        cut.WaitForAssertion(() => Assert.Single(_pages));
        Answer(cut, _pages[0].Result, OperationResult<SpaceChildrenPageViewModel>.Ok(FirstPage()));
        OpenTab(cut);

        cut.Find(".space-children-more").Click();

        Assert.Equal((Space, "next"), (_pages[1].RoomId, _pages[1].From));
        Answer(cut, _pages[1].Result, OperationResult<SpaceChildrenPageViewModel>.Ok(new SpaceChildrenPageViewModel
        {
            Summaries = [new() { RoomId = "!remote:other", Name = "Remote", JoinedMembers = 9 }],
        }));
        var row = Rows(cut)[2];
        Assert.Equal(("Remote", "9"), (WebUtility.HtmlDecode(row.QuerySelector("a")!.TextContent).Trim(), Cell(row, 2)));
        Assert.Equal(3, Rows(cut).Count);
        Assert.Empty(cut.FindAll(".space-children-more"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("next")]
    public void LoadMore_IsHiddenWithoutANewToken(string? secondToken)
    {
        var cut = RenderRoom(Space);
        cut.WaitForAssertion(() => Assert.Single(_pages));
        Answer(cut, _pages[0].Result, OperationResult<SpaceChildrenPageViewModel>.Ok(FirstPage()));
        OpenTab(cut);
        cut.Find(".space-children-more").Click();

        Answer(cut, _pages[1].Result, OperationResult<SpaceChildrenPageViewModel>.Ok(new SpaceChildrenPageViewModel { NextBatch = secondToken }));

        Assert.Empty(cut.FindAll(".space-children-more"));
    }

    [Fact]
    public void FirstPageWithoutTheSpace_SaysThisServerIsntInIt()
    {
        var cut = RenderRoom(Space);
        cut.WaitForAssertion(() => Assert.Single(_pages));
        Answer(cut, _pages[0].Result, OperationResult<SpaceChildrenPageViewModel>.Ok(new SpaceChildrenPageViewModel()));

        OpenTab(cut);

        Assert.Equal(L["SpaceNotOnThisServer"].Value, WebUtility.HtmlDecode(cut.Find(".space-not-on-server .mud-alert-message").TextContent).Trim());
        Assert.Empty(Rows(cut));
    }

    [Fact]
    public void SpaceWithoutChildren_SaysSo()
    {
        var cut = RenderRoom(Space);
        cut.WaitForAssertion(() => Assert.Single(_pages));
        Answer(cut, _pages[0].Result, OperationResult<SpaceChildrenPageViewModel>.Ok(new SpaceChildrenPageViewModel { Children = [] }));

        OpenTab(cut);

        Assert.Contains(L["SpaceHasNoChildren"].Value, WebUtility.HtmlDecode(cut.Markup));
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("warning")]
    public void FailedLoad_ShowsInTheTab_WithoutASnackbar(string kind)
    {
        var result = kind == "warning"
            ? OperationResult<SpaceChildrenPageViewModel>.Failure("Not supported", Severity.Warning)
            : Make<OperationResult<SpaceChildrenPageViewModel>>(kind);
        var cut = RenderRoom(Space);
        cut.WaitForAssertion(() => Assert.Single(_pages));
        Answer(cut, _pages[0].Result, result);

        OpenTab(cut);

        var alert = Assert.Single(cut.FindComponents<MudAlert>());
        Assert.Equal((result.Severity, result.Message), (alert.Instance.Severity, WebUtility.HtmlDecode(alert.Find(".mud-alert-message").TextContent).Trim()));
        Assert.Contains($"Room {Space}", cut.Markup);
        AssertSnackbars();
    }

    /// <summary>The next room is a space too, so a late answer for the previous one would show in its tab.</summary>
    [Fact]
    public void LateAnswer_ForASpaceNoLongerShown_ChangesNothing()
    {
        var cut = RenderRoom(Space);
        cut.WaitForAssertion(() => Assert.Single(_pages));
        var previous = _pages[0];

        cut.Render(p => p.Add(x => x.RoomId, OtherSpace));
        cut.WaitForAssertion(() => Assert.Equal(2, _pages.Count));
        Assert.True(previous.Token.IsCancellationRequested);
        Assert.Equal(OtherSpace, _pages[1].RoomId);
        Answer(cut, _pages[1].Result, OperationResult<SpaceChildrenPageViewModel>.Ok(new SpaceChildrenPageViewModel
        {
            Children = [new() { RoomId = "!only:hs" }],
        }));
        Answer(cut, previous.Result, OperationResult<SpaceChildrenPageViewModel>.Ok(FirstPage()));
        OpenTab(cut);

        Assert.Equal(["!only:hs"], Rows(cut).Select(r => r.GetAttribute("data-room-id")));
        Assert.Empty(cut.FindAll(".space-children-more"));
    }

    [Fact]
    public void SwitchingToARoomThatIsNoSpace_RemovesTheTab()
    {
        var cut = RenderRoom(Space);
        cut.WaitForAssertion(() => Assert.Single(_pages));

        cut.Render(p => p.Add(x => x.RoomId, PlainRoom));
        cut.WaitForAssertion(() => Assert.Contains($"Room {PlainRoom}", cut.Markup));

        Assert.True(_pages[0].Token.IsCancellationRequested);
        Assert.DoesNotContain(cut.FindAll(".mud-tab"), t => WebUtility.HtmlDecode(t.TextContent).Trim() == L["SpaceChildren"].Value);
        Assert.Single(_pages);
    }
}
