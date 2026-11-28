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
/// The room list's public and empty room filters (#588, AC-30): each is All, Only or Exclude, goes to the service as
/// <c>null</c>, <c>true</c> or <c>false</c>, combines with the search term and the sort order, and reloads the list
/// from the first page, as a new search term does.
/// </summary>
public class RoomListFilterTests : DetailPageTestBase
{
    private const int ArgOffset = 0;
    private const int ArgOrderBy = 2;
    private const int ArgDirection = 3;
    private const int ArgSearchTerm = 4;
    private const int ArgPublicRooms = 5;
    private const int ArgEmptyRooms = 6;

    private readonly Stub _rooms;

    public RoomListFilterTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        // More rooms than one page holds, so that the pager can leave the first page.
        _rooms.On(nameof(IRoomService.GetRoomListAsync), _ => Task.FromResult(OperationResult<(int, List<RoomListViewModel>)>.Ok(
            (100, Enumerable.Range(0, 10).Select(i => new RoomListViewModel { RoomId = $"!r{i}:hs", Name = $"R{i}" }).ToList()))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IRenderedComponent<Rooms> RenderRooms()
    {
        var cut = Render<Rooms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(_rooms.CallsTo(nameof(IRoomService.GetRoomListAsync))));
        return cut;
    }

    private object?[] LastCall() => _rooms.CallsTo(nameof(IRoomService.GetRoomListAsync)).Last();

    private IRenderedComponent<MudSelect<RoomListFilterChoice>> Filter(IRenderedComponent<Rooms> cut, string labelKey) =>
        cut.FindComponents<MudSelect<RoomListFilterChoice>>().Single(s => s.Instance.Label == L[labelKey].Value);

    private static Task Choose(IRenderedComponent<Rooms> cut, IRenderedComponent<MudSelect<RoomListFilterChoice>> filter, RoomListFilterChoice choice) =>
        cut.InvokeAsync(() => filter.Instance.ValueChanged.InvokeAsync(choice));

    private static Task Search(IRenderedComponent<Rooms> cut, string term) =>
        cut.InvokeAsync(() => cut.FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync(term));

    private static Task GoToPage(IRenderedComponent<Rooms> cut, int page) =>
        cut.InvokeAsync(() => cut.FindComponent<MudTable<RoomListViewModel>>().Instance.NavigateTo(page));

    [Fact]
    public void Rooms_ByDefault_SendsNeitherFlag()
    {
        var cut = RenderRooms();

        var args = LastCall();
        Assert.Null(args[ArgPublicRooms]);
        Assert.Null(args[ArgEmptyRooms]);
    }

    [Theory]
    [InlineData("RoomListFilterPublicRooms", RoomListFilterChoice.Only, true, null)]
    [InlineData("RoomListFilterPublicRooms", RoomListFilterChoice.Exclude, false, null)]
    [InlineData("RoomListFilterEmptyRooms", RoomListFilterChoice.Only, null, true)]
    [InlineData("RoomListFilterEmptyRooms", RoomListFilterChoice.Exclude, null, false)]
    public async Task Rooms_ChoosingAFilter_SendsItsFlag(string labelKey, RoomListFilterChoice choice, bool? publicRooms, bool? emptyRooms)
    {
        var cut = RenderRooms();

        await Choose(cut, Filter(cut, labelKey), choice);

        cut.WaitForAssertion(() => Assert.Equal((publicRooms, emptyRooms), ((bool?)LastCall()[ArgPublicRooms], (bool?)LastCall()[ArgEmptyRooms])));
    }

    [Fact]
    public async Task Rooms_ChoosingAllAgain_LeavesTheFlagOut()
    {
        var cut = RenderRooms();
        await Choose(cut, Filter(cut, "RoomListFilterPublicRooms"), RoomListFilterChoice.Only);

        await Choose(cut, Filter(cut, "RoomListFilterPublicRooms"), RoomListFilterChoice.All);

        cut.WaitForAssertion(() => Assert.Null(LastCall()[ArgPublicRooms]));
    }

    [Fact]
    public async Task Rooms_Filters_CombineWithEachOtherTheSearchTermAndTheSortOrder()
    {
        var cut = RenderRooms();
        await Search(cut, "lobby");
        cut.FindComponents<MudTableSortLabel<RoomListViewModel>>().Single(c => c.Instance.SortLabel == "joined_members")
            .Find(".mud-table-sort-label").Click();

        await Choose(cut, Filter(cut, "RoomListFilterPublicRooms"), RoomListFilterChoice.Exclude);
        await Choose(cut, Filter(cut, "RoomListFilterEmptyRooms"), RoomListFilterChoice.Only);

        cut.WaitForAssertion(() =>
        {
            var args = LastCall();
            Assert.Equal(("lobby", "joined_members", SortDirection.Ascending, false, true),
                ((string?)args[ArgSearchTerm], (string?)args[ArgOrderBy], (SortDirection?)args[ArgDirection], (bool?)args[ArgPublicRooms], (bool?)args[ArgEmptyRooms]));
        });
    }

    [Fact]
    public async Task Rooms_ChoosingAFilter_ReloadsFromTheFirstPage()
    {
        var cut = RenderRooms();
        await GoToPage(cut, 3);
        cut.WaitForAssertion(() => Assert.Equal(30, (int?)LastCall()[ArgOffset]));

        var callsBefore = _rooms.CallsTo(nameof(IRoomService.GetRoomListAsync)).Count;

        await Choose(cut, Filter(cut, "RoomListFilterEmptyRooms"), RoomListFilterChoice.Only);

        cut.WaitForAssertion(() => Assert.Equal((0, true), ((int?)LastCall()[ArgOffset], (bool?)LastCall()[ArgEmptyRooms])));
        Assert.Equal(callsBefore + 1, _rooms.CallsTo(nameof(IRoomService.GetRoomListAsync)).Count);
    }

    [Fact]
    public async Task Rooms_ChangingTheSearchTerm_ReloadsFromTheFirstPage()
    {
        var cut = RenderRooms();
        await GoToPage(cut, 3);
        cut.WaitForAssertion(() => Assert.Equal(30, (int?)LastCall()[ArgOffset]));

        await Search(cut, "lobby");

        cut.WaitForAssertion(() => Assert.Equal((0, "lobby"), ((int?)LastCall()[ArgOffset], (string?)LastCall()[ArgSearchTerm])));
    }

    [Fact]
    public async Task Rooms_WithAFilter_CountsMatchingRooms()
    {
        var cut = RenderRooms();
        cut.WaitForAssertion(() => Assert.Contains(string.Format(L["TotalRoomsOnServer"], 100), WebUtility.HtmlDecode(cut.Markup)));

        await Choose(cut, Filter(cut, "RoomListFilterPublicRooms"), RoomListFilterChoice.Only);

        cut.WaitForAssertion(() =>
        {
            var markup = WebUtility.HtmlDecode(cut.Markup);
            Assert.Contains(string.Format(L["MatchingRoomsCount"], 100), markup);
            Assert.DoesNotContain(string.Format(L["TotalRoomsOnServer"], 100), markup);
        });
    }
}
