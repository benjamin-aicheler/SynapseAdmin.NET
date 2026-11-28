using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The event report list's filters (#588, AC-31): room ID, reporter and reported user, each sent in its own field of
/// <see cref="EventReportListFilter"/>, combined, and reloading the list from the first page.
/// </summary>
public class EventReportsFilterTests : DetailPageTestBase
{
    private const int ArgOffset = 0;
    private const int ArgFilter = 3;

    private readonly Stub _reports;

    public EventReportsFilterTests()
    {
        Services.AddSingleton(Stub.Create<IEventReportService>(out _reports));
        // More reports than one page holds, so that the pager can leave the first page.
        _reports.On(nameof(IEventReportService.GetEventReportsAsync), _ => Task.FromResult(OperationResult<(int, List<EventReportListViewModel>)>.Ok(
            (100, Enumerable.Range(0, 10).Select(i => new EventReportListViewModel { Id = $"{i}", EventId = $"$e{i}", RoomId = "!r:hs" }).ToList()))));

        // The dashboard's other reads, for the test that renders it.
        Services.AddSingleton(Stub.Create<IRoomService>(r => r
            .On(nameof(IRoomService.GetRoomListAsync), _ => Task.FromResult(OperationResult<(int, List<RoomListViewModel>)>.Ok((0, []))))
            .On(nameof(IRoomService.GetLargestRoomsAsync), _ => Task.FromResult(OperationResult<List<RoomStatisticsViewModel>>.Ok([])))));
        Services.AddSingleton(Stub.Create<IUserService>(u => u
            .On(nameof(IUserService.GetUserListAsync), _ => Task.FromResult(OperationResult<(int, List<UserListViewModel>)>.Ok((0, []))))
            .On(nameof(IUserService.GetTopMediaUsersAsync), _ => Task.FromResult(OperationResult<List<UserMediaStatisticsViewModel>>.Ok([])))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IRenderedComponent<EventReports> RenderReports()
    {
        var cut = Render<EventReports>();
        cut.WaitForAssertion(() => Assert.NotEmpty(_reports.CallsTo(nameof(IEventReportService.GetEventReportsAsync))));
        return cut;
    }

    private object?[] LastCall() => _reports.CallsTo(nameof(IEventReportService.GetEventReportsAsync)).Last();

    private EventReportListFilter? LastFilter() => (EventReportListFilter?)LastCall()[ArgFilter];

    private Task Type(IRenderedComponent<EventReports> cut, string labelKey, string text)
    {
        var field = cut.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == L[labelKey].Value);
        return cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync(text));
    }

    [Fact]
    public void Reports_ByDefault_SendNoFilterValue()
    {
        RenderReports();

        Assert.Equal(new EventReportListFilter(), LastFilter());
    }

    [Theory]
    [InlineData("EventReportFilterRoomId", "!room:hs", "!room:hs", null, null)]
    [InlineData("EventReportFilterReporter", "alice", null, "alice", null)]
    [InlineData("EventReportFilterSender", "@bad:hs", null, null, "@bad:hs")]
    public async Task Reports_EachField_GoesToItsFilter(string labelKey, string text, string? roomId, string? reporter, string? sender)
    {
        var cut = RenderReports();

        await Type(cut, labelKey, text);

        cut.WaitForAssertion(() => Assert.Equal(new EventReportListFilter(roomId, reporter, sender), LastFilter()));
    }

    [Fact]
    public async Task Reports_Fields_Combine()
    {
        var cut = RenderReports();

        await Type(cut, "EventReportFilterRoomId", "!room:hs");
        await Type(cut, "EventReportFilterReporter", "alice");
        await Type(cut, "EventReportFilterSender", "@bad:hs");

        cut.WaitForAssertion(() => Assert.Equal(new EventReportListFilter("!room:hs", "alice", "@bad:hs"), LastFilter()));
    }

    [Fact]
    public async Task Reports_ChangingAField_ReloadsFromTheFirstPage_WithOneRequest()
    {
        var cut = RenderReports();
        await cut.InvokeAsync(() => cut.FindComponent<MudTable<EventReportListViewModel>>().Instance.NavigateTo(3));
        cut.WaitForAssertion(() => Assert.Equal(30, (int?)LastCall()[ArgOffset]));
        var callsBefore = _reports.CallsTo(nameof(IEventReportService.GetEventReportsAsync)).Count;

        await Type(cut, "EventReportFilterSender", "@bad:hs");

        cut.WaitForAssertion(() => Assert.Equal((0, "@bad:hs"), ((int?)LastCall()[ArgOffset], LastFilter()?.SenderUserId)));
        Assert.Equal(callsBefore + 1, _reports.CallsTo(nameof(IEventReportService.GetEventReportsAsync)).Count);
    }

    [Fact]
    public async Task Reports_WithAField_CountMatchingReports()
    {
        var cut = RenderReports();
        cut.WaitForAssertion(() => Assert.Contains(string.Format(L["TotalReports"], 100), WebUtility.HtmlDecode(cut.Markup)));

        await Type(cut, "EventReportFilterReporter", "alice");

        cut.WaitForAssertion(() =>
        {
            var markup = WebUtility.HtmlDecode(cut.Markup);
            Assert.Contains(string.Format(L["MatchingEventReportsCount"], 100), markup);
            Assert.DoesNotContain(string.Format(L["TotalReports"], 100), markup);
        });
    }

    [Fact]
    public async Task Reports_ABlankField_CountsEveryReport()
    {
        var cut = RenderReports();

        await Type(cut, "EventReportFilterRoomId", "  ");

        cut.WaitForAssertion(() => Assert.Contains(string.Format(L["TotalReports"], 100), WebUtility.HtmlDecode(cut.Markup)));
    }

    [Fact]
    public void Dashboard_LatestReports_SendNoFilter()
    {
        Render<Home>();

        var args = Assert.Single(_reports.CallsTo(nameof(IEventReportService.GetEventReportsAsync)));
        Assert.Null(args[ArgFilter]);
    }
}
