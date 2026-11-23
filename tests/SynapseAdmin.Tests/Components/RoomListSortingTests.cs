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
/// The sort keys the room list and the dashboard request. Synapse rejects <c>order_by=room_type</c> (#540) and has no
/// <c>room_id</c> key (#545), so Room Type and Room ID are plain headers and the dashboard counts rooms by name.
/// Which <c>dir</c> a key gets is the service's and the gateway's job (#541, <c>SortDirectionMappingTests</c>).
/// </summary>
public class RoomListSortingTests : DetailPageTestBase
{
    private readonly Stub _rooms;

    public RoomListSortingTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomListAsync), _ => Task.FromResult(OperationResult<(int, List<RoomListViewModel>)>.Ok(
                (1, [new RoomListViewModel { RoomId = "!a:hs", Name = "A", RoomType = "m.space" }]))))
            .On(nameof(IRoomService.GetLargestRoomsAsync), _ => Task.FromResult(OperationResult<List<RoomStatisticsViewModel>>.Ok([])));
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.GetUserListAsync), _ => Task.FromResult(OperationResult<(int, List<UserListViewModel>)>.Ok((0, []))))
            .On(nameof(IUserService.GetTopMediaUsersAsync), _ => Task.FromResult(OperationResult<List<UserMediaStatisticsViewModel>>.Ok([])))));
        Services.AddSingleton(Stub.Create<IEventReportService>(s => s
            .On(nameof(IEventReportService.GetEventReportsAsync),
                _ => Task.FromResult(OperationResult<(int, List<EventReportListViewModel>)>.Ok((0, []))))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IRenderedComponent<Rooms> RenderRooms()
    {
        var cut = Render<Rooms>();
        cut.WaitForAssertion(() => Assert.NotEmpty(_rooms.CallsTo(nameof(IRoomService.GetRoomListAsync))));
        return cut;
    }

    [Fact]
    public void Rooms_SortsOnlyByKeysTheAdminApiDocuments()
    {
        var cut = RenderRooms();

        var sortLabels = cut.FindComponents<MudTableSortLabel<RoomListViewModel>>().Select(c => c.Instance.SortLabel);
        Assert.Equal(["name", "creator", "joined_members", "public"], sortLabels);
    }

    [Fact]
    public void Rooms_ShowsRoomIdAndRoomTypeAsPlainHeaders()
    {
        var cut = RenderRooms();

        var headers = cut.FindAll("th").ToDictionary(th => WebUtility.HtmlDecode(th.TextContent).Trim());
        foreach (var key in new[] { "RoomId", "RoomType" })
        {
            var header = headers[L[key].Value];
            Assert.Empty(header.QuerySelectorAll(".mud-table-sort-label"));
        }
        cut.WaitForAssertion(() => Assert.Contains("m.space", cut.Markup));
    }

    [Fact]
    public void Rooms_ClickingMembers_RequestsJoinedMembersAscending()
    {
        var cut = RenderRooms();
        var members = cut.FindComponents<MudTableSortLabel<RoomListViewModel>>().Single(c => c.Instance.SortLabel == "joined_members");

        members.Find(".mud-table-sort-label").Click();

        cut.WaitForAssertion(() =>
        {
            var args = _rooms.CallsTo(nameof(IRoomService.GetRoomListAsync)).Last();
            Assert.Equal(("joined_members", SortDirection.Ascending), ((string?)args[2], (SortDirection?)args[3]));
        });
    }

    [Fact]
    public void Dashboard_CountsRoomsWithTheDocumentedNameKey()
    {
        Render<Home>();

        var args = Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetRoomListAsync)));
        Assert.Equal((0, 1, "name"), ((int?)args[0], (int?)args[1], (string?)args[2]));
    }
}
