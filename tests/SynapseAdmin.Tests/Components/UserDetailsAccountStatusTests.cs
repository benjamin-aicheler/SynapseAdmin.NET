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
/// The Status row of the user page (#584, AC-19): Suspended and Shadow-banned next to Deactivated and Locked. Active
/// means neither deactivated, locked nor suspended; a shadow-banned account is still active.
/// </summary>
public class UserDetailsAccountStatusTests : DetailPageTestBase
{
    private const string UserId = "@alice:hs";
    private UserDetailViewModel _user = new() { UserId = UserId };

    public UserDetailsAccountStatusTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.GetUserDetailsAsync), _ => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(_user)))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private string[] StatusChips(IRenderedComponent<UserDetails> cut)
    {
        var status = cut.FindAll("b").Single(b => WebUtility.HtmlDecode(b.TextContent).Trim() == $"{L["Status"].Value}:");
        // The chips are <div>s inside MudBlazor's <p>, which the HTML parser closes early: take the whole list item.
        return status.Closest(".mud-list-item")!.QuerySelectorAll(".mud-chip")
            .Select(c => WebUtility.HtmlDecode(c.TextContent).Trim()).ToArray();
    }

    [Theory]
    [InlineData(false, false, false, false, new[] { "Active" })]
    [InlineData(false, false, true, false, new[] { "Suspended" })]
    [InlineData(false, false, false, true, new[] { "Active", "ShadowBanned" })]
    [InlineData(false, false, true, true, new[] { "Suspended", "ShadowBanned" })]
    [InlineData(false, true, true, false, new[] { "Locked", "Suspended" })]
    [InlineData(true, false, false, true, new[] { "Deactivated", "ShadowBanned" })]
    [InlineData(true, true, true, true, new[] { "Deactivated", "Locked", "Suspended", "ShadowBanned" })]
    public void Status_ShowsSuspendedAndShadowBanned(bool deactivated, bool locked, bool suspended, bool shadowBanned, string[] expectedKeys)
    {
        _user = new UserDetailViewModel
        {
            UserId = UserId,
            Deactivated = deactivated,
            Locked = locked,
            Suspended = suspended,
            ShadowBanned = shadowBanned
        };

        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, UserId));

        cut.WaitForAssertion(() => Assert.Equal(expectedKeys.Select(k => L[k].Value), StatusChips(cut)));
    }
}
