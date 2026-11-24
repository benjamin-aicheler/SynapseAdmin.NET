using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which results the user, room, event report and federation lists report in a snackbar: a list load reports only
/// failures, also not a success without data, while dismissing a report or resetting a connection reports every result
/// except a silent cancellation.
/// </summary>
public class ListPageSnackbarTests : DetailPageTestBase
{
    private readonly Stub _users;
    private readonly Stub _rooms;
    private readonly Stub _reports;
    private readonly Stub _federation;
    private OperationResult _result = new();
    private OperationResult<(int Total, List<UserListViewModel> Users)> _userList = OperationResult<(int, List<UserListViewModel>)>.Ok((0, []));
    private OperationResult<(int Total, List<RoomListViewModel> Rooms)> _roomList = OperationResult<(int, List<RoomListViewModel>)>.Ok((0, []));
    private OperationResult<(int Total, List<EventReportListViewModel> Reports)> _reportList = OperationResult<(int, List<EventReportListViewModel>)>.Ok((0, []));
    private OperationResult<List<FederationDestinationListViewModel>> _destinations = OperationResult<List<FederationDestinationListViewModel>>.Ok([]);

    public ListPageSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users.On(nameof(IUserService.GetUserListAsync), _ => Task.FromResult(_userList));
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms.On(nameof(IRoomService.GetRoomListAsync), _ => Task.FromResult(_roomList));
        Services.AddSingleton(Stub.Create<IEventReportService>(out _reports));
        _reports
            .On(nameof(IEventReportService.GetEventReportsAsync), _ => Task.FromResult(_reportList))
            .On(nameof(IEventReportService.DeleteEventReportAsync), _ => Task.FromResult(_result));
        Services.AddSingleton(Stub.Create<IFederationService>(out _federation));
        _federation
            .On(nameof(IFederationService.GetAllDestinationsAsync), _ => Task.FromResult(_destinations))
            .On(nameof(IFederationService.ResetConnectionTimeoutAsync), _ => Task.FromResult(_result));
    }

    /// <summary>Renders the page and waits until its table has requested the list.</summary>
    private IRenderedComponent<TPage> RenderList<TPage>(Stub service, string listMethod) where TPage : IComponent
    {
        var cut = Render<TPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(service.CallsTo(listMethod)));
        return cut;
    }

    [Fact]
    public async Task CreateUserDialog_IsSmall_AndClosesOnEscape()
    {
        CancelDialogs();
        var cut = RenderList<Users>(_users, nameof(IUserService.GetUserListAsync));

        await InvokeHandler(cut, "OpenCreateUserDialog");

        AssertDialogOptions(new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
    }

    public static TheoryData<string, string> Loads()
    {
        var data = new TheoryData<string, string>();
        foreach (var page in new[] { nameof(Users), nameof(Rooms), nameof(EventReports), nameof(FederationDestinations) })
        {
            foreach (var kind in new[] { "success", "failure", "cancelled" }) data.Add(page, kind);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Loads))]
    public void ListLoad_ReportsOnlyFailures(string page, string kind)
    {
        OperationResult result;
        switch (page)
        {
            case nameof(Users):
                result = _userList = Make<OperationResult<(int, List<UserListViewModel>)>>(kind);
                RenderList<Users>(_users, nameof(IUserService.GetUserListAsync));
                break;
            case nameof(Rooms):
                result = _roomList = Make<OperationResult<(int, List<RoomListViewModel>)>>(kind);
                RenderList<Rooms>(_rooms, nameof(IRoomService.GetRoomListAsync));
                break;
            case nameof(EventReports):
                result = _reportList = Make<OperationResult<(int, List<EventReportListViewModel>)>>(kind);
                RenderList<EventReports>(_reports, nameof(IEventReportService.GetEventReportsAsync));
                break;
            default:
                result = _destinations = Make<OperationResult<List<FederationDestinationListViewModel>>>(kind);
                RenderList<FederationDestinations>(_federation, nameof(IFederationService.GetAllDestinationsAsync));
                break;
        }

        AssertSnackbars(kind == "failure" ? [result] : []);
    }

    /// <summary>After a success the list reloads, which reports nothing.</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public async Task DismissReport_ReportsEveryResultExceptACancellation(string kind)
    {
        _result = Make<OperationResult>(kind);
        var cut = RenderList<EventReports>(_reports, nameof(IEventReportService.GetEventReportsAsync));

        await ConfirmAsync(cut, "DeleteReport", "report1");

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public async Task ResetConnection_ReportsEveryResultExceptACancellation(string kind)
    {
        _result = Make<OperationResult>(kind);
        var cut = RenderList<FederationDestinations>(_federation, nameof(IFederationService.GetAllDestinationsAsync));

        await ConfirmAsync(cut, "ResetConnection", "remote.org");

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }

    private async Task ConfirmAsync<TPage>(IRenderedComponent<TPage> cut, string handler, string argument) where TPage : IComponent
    {
        var action = InvokeHandler(cut, handler, argument);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;
    }
}
