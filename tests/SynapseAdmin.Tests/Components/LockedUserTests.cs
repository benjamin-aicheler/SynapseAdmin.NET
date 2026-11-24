using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The user list includes locked accounts (#547), so the list and the user page mark them as Locked, apart from
/// Deactivated. The page shows Active only for an account that is neither.
/// </summary>
public class LockedUserTests : DetailPageTestBase
{
    private const string UserId = "@alice:hs";
    private UserDetailViewModel _user = new() { UserId = UserId, DisplayName = "Alice Example" };

    public LockedUserTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.GetUserListAsync), _ => Task.FromResult(OperationResult<(int, List<UserListViewModel>)>.Ok((2,
            [
                new UserListViewModel { UserId = "@locked:hs", Locked = true },
                new UserListViewModel { UserId = "@open:hs" },
            ]))))
            .On(nameof(IUserService.GetUserDetailsAsync), _ => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(_user)))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static string[] Chips(AngleSharp.Dom.IElement element) =>
        element.QuerySelectorAll(".mud-chip").Select(c => WebUtility.HtmlDecode(c.TextContent).Trim()).ToArray();

    [Fact]
    public void UserList_MarksOnlyLockedUsers()
    {
        var cut = Render<Users>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.Contains(L["Locked"].Value, Chips(rows.Single(r => r.TextContent.Contains("@locked:hs"))));
            Assert.DoesNotContain(L["Locked"].Value, Chips(rows.Single(r => r.TextContent.Contains("@open:hs"))));
        });
        var headers = cut.FindAll("th").Select(th => WebUtility.HtmlDecode(th.TextContent).Trim());
        Assert.Contains(L["Locked"].Value, headers);
    }

    [Theory]
    [InlineData(false, false, new[] { "Active" })]
    [InlineData(true, false, new[] { "Locked" })]
    [InlineData(false, true, new[] { "Deactivated" })]
    [InlineData(true, true, new[] { "Deactivated", "Locked" })]
    public void UserDetails_StatusShowsLockedApartFromDeactivated(bool locked, bool deactivated, string[] expectedKeys)
    {
        _user = new UserDetailViewModel { UserId = UserId, DisplayName = "Alice Example", Locked = locked, Deactivated = deactivated };

        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, UserId));

        cut.WaitForAssertion(() =>
        {
            var status = cut.FindAll("b").Single(b => WebUtility.HtmlDecode(b.TextContent).Trim() == $"{L["Status"].Value}:");
            // The chips are <div>s inside MudBlazor's <p>, which the HTML parser closes early: take the whole list item.
            Assert.Equal(expectedKeys.Select(k => L[k].Value), Chips(status.Closest(".mud-list-item")!));
        });
    }
}
