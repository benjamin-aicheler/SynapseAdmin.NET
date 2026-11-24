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
/// The numbered user and event report tables send <c>from = page × pageSize</c>, not <c>next_token</c>, so the pager
/// can jump to any page. Synapse documents <c>from</c> as opaque, but Synapse and Tuwunel implement it as an item
/// offset (#551, AGENTS.md Known Pitfalls).
/// </summary>
public class ListPageOffsetTests : DetailPageTestBase
{
    private readonly Stub _users;
    private readonly Stub _reports;

    public ListPageOffsetTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users.On(nameof(IUserService.GetUserListAsync), _ =>
            Task.FromResult(OperationResult<(int, List<UserListViewModel>)>.Ok((0, []))));
        Services.AddSingleton(Stub.Create<IEventReportService>(out _reports));
        _reports.On(nameof(IEventReportService.GetEventReportsAsync), _ =>
            Task.FromResult(OperationResult<(int, List<EventReportListViewModel>)>.Ok((0, []))));
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(2, 25, 50)]
    [InlineData(7, 100, 700)]
    public async Task Users_SendPageTimesPageSizeAsOffset(int page, int pageSize, int offset)
    {
        var cut = Render<Users>();
        cut.WaitForAssertion(() => Assert.NotEmpty(_users.CallsTo(nameof(IUserService.GetUserListAsync))));

        await InvokeHandler(cut, "ServerReload", new TableState { Page = page, PageSize = pageSize }, CancellationToken.None);

        var call = _users.CallsTo(nameof(IUserService.GetUserListAsync))[^1];
        Assert.Equal((offset, pageSize), ((int)call[0]!, (int)call[1]!));
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(2, 25, 50)]
    [InlineData(7, 100, 700)]
    public async Task EventReports_SendPageTimesPageSizeAsOffset(int page, int pageSize, int offset)
    {
        var cut = Render<EventReports>();
        cut.WaitForAssertion(() => Assert.NotEmpty(_reports.CallsTo(nameof(IEventReportService.GetEventReportsAsync))));

        await InvokeHandler(cut, "ServerReload", new TableState { Page = page, PageSize = pageSize }, CancellationToken.None);

        var call = _reports.CallsTo(nameof(IEventReportService.GetEventReportsAsync))[^1];
        Assert.Equal((offset, pageSize), ((int)call[0]!, (int)call[1]!));
    }
}
