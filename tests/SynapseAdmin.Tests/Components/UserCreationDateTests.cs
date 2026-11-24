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
/// Tuwunel stores no account creation time and refuses to sort the user list by anything but user ID (AC-17). The pages
/// show an unknown creation date as "unknown" instead of 1970, and the dashboard keeps its user count when the latest
/// users are refused.
/// </summary>
public class UserCreationDateTests : DetailPageTestBase
{
    private const string UserId = "@alice:hs";
    private const long KnownCreationMs = 1_700_000_000_000;

    private readonly Stub _users;
    private long? _creationTs;
    private OperationResult<(int, List<UserListViewModel>)>? _latestUsers;

    public UserCreationDateTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserListAsync), args => Task.FromResult(
                (string)args[2]! == "creation_ts" && _latestUsers != null
                    ? _latestUsers
                    : OperationResult<(int, List<UserListViewModel>)>.Ok((42, [new UserListViewModel { UserId = UserId, CreationTs = _creationTs }]))))
            .On(nameof(IUserService.GetUserDetailsAsync), _ => Task.FromResult(
                OperationResult<UserDetailViewModel>.Ok(new UserDetailViewModel { UserId = UserId, CreationTs = _creationTs })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())))
            .On(nameof(IUserService.GetTopMediaUsersAsync), _ => Task.FromResult(OperationResult<List<UserMediaStatisticsViewModel>>.Ok([])));
        Services.AddSingleton(Stub.Create<IRoomService>(s => s
            .On(nameof(IRoomService.GetRoomListAsync), _ => Task.FromResult(OperationResult<(int, List<RoomListViewModel>)>.Ok((0, []))))
            .On(nameof(IRoomService.GetLargestRoomsAsync), _ => Task.FromResult(OperationResult<List<RoomStatisticsViewModel>>.Ok([])))));
        Services.AddSingleton(Stub.Create<IEventReportService>(s => s
            .On(nameof(IEventReportService.GetEventReportsAsync),
                _ => Task.FromResult(OperationResult<(int, List<EventReportListViewModel>)>.Ok((0, []))))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();
    private string Unknown => L["CreationDateUnknown"].Value;
    private static string Text(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut) => WebUtility.HtmlDecode(cut.Markup);

    public static TheoryData<string> Pages => new() { "Users", "UserDetails", "Home" };

    private IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> RenderPage(string page) => page switch
    {
        "Users" => Render<Users>(),
        "UserDetails" => Render<UserDetails>(p => p.Add(x => x.UserId, UserId)),
        "Home" => Render<Home>(),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null)
    };

    [Theory, MemberData(nameof(Pages))]
    public void UnknownCreationDate_IsShownAsUnknown(string page)
    {
        _creationTs = null;

        var cut = RenderPage(page);

        cut.WaitForAssertion(() => Assert.Contains(UserId, Text(cut)));
        Assert.Contains(Unknown, Text(cut));
        Assert.DoesNotContain("1970", Text(cut));
    }

    [Theory, MemberData(nameof(Pages))]
    public void KnownCreationDate_IsShownAsADate(string page)
    {
        _creationTs = KnownCreationMs;

        var cut = RenderPage(page);

        cut.WaitForAssertion(() => Assert.Contains(UserId, Text(cut)));
        Assert.DoesNotContain(Unknown, Text(cut));
        Assert.Contains("2023", Text(cut));
    }

    /// <summary>
    /// The dashboard counts users with its own request by name, so a homeserver that refuses the latest users (sorted
    /// by creation time) still gets its count, and the refusal shows no snackbar.
    /// </summary>
    [Fact]
    public void Dashboard_LatestUsersRefused_KeepsTheUserCount_WithoutASnackbar()
    {
        _latestUsers = OperationResult<(int, List<UserListViewModel>)>.Failure(L["OperationNotSupportedByHomeserver"].Value, Severity.Warning);

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("42", cut.FindAll(".mud-typography-h4").Select(e => e.TextContent.Trim())));
        Assert.DoesNotContain(UserId, Text(cut));
        Assert.Empty(SnackbarStub.CallsTo(nameof(ISnackbar.Add)));
        var requests = _users.CallsTo(nameof(IUserService.GetUserListAsync)).Select(a => ((int?)a[0], (int?)a[1], (string?)a[2]));
        Assert.Contains((0, 1, "name"), requests);
        Assert.Contains((0, 5, "creation_ts"), requests);
    }
}
