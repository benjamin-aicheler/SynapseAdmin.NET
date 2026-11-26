using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The account status actions on the user page (#584, AC-20): Lock or Unlock, Suspend or Lift suspension, Make or
/// Remove server admin, Shadow-ban or Lift shadow-ban, each following the state shown, confirmed for the displayed user
/// and followed by a reload. Actions that would restrict the signed-in admin's own account are disabled there.
/// </summary>
public class UserDetailsAccountActionsTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";

    private readonly Stub _users;
    private UserDetailViewModel _shown = new() { UserId = UserA, DisplayName = "Alice Example" };
    private OperationResult _actionResult = OperationResult.Ok("Done");

    public UserDetailsAccountActionsTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), args => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                (string)args[0]! == UserA ? _shown : new UserDetailViewModel { UserId = UserB, DisplayName = "Bob Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())))
            .On(nameof(IUserService.SetUserLockedAsync), _ => Task.FromResult(_actionResult))
            .On(nameof(IUserService.SetUserSuspendedAsync), _ => Task.FromResult(_actionResult))
            .On(nameof(IUserService.SetUserAdminAsync), _ => Task.FromResult(_actionResult))
            .On(nameof(IUserService.SetUserShadowBannedAsync), _ => Task.FromResult(_actionResult))
            .On(nameof(IUserService.DeactivateUserAsync), _ => Task.FromResult(_actionResult));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private int DetailReads => _users.CallsTo(nameof(IUserService.GetUserDetailsAsync)).Count;

    /// <summary>Signs in as <paramref name="userId"/>: the session's gateway reports it as its own user.</summary>
    private void SignInAs(string userId)
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_SupportsAdminApi", _ => true)
            .On("get_ServerName", _ => "hs")
            .On("get_UserId", _ => userId));
        Services.AddSingleton(Stub.Create<IMatrixSessionService>(s => s
            .On("get_Gateway", _ => gateway)
            .On("get_IsLoggedIn", _ => true)));
    }

    private IRenderedComponent<UserDetails> RenderUser(string userId = UserA)
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, userId));
        cut.WaitForAssertion(() => Assert.Contains(userId == UserA ? "Alice Example" : "Bob Example", cut.Markup));
        return cut;
    }

    private AngleSharp.Dom.IElement Button(IRenderedComponent<UserDetails> cut, string key) =>
        cut.FindAll("button").Single(b => WebUtility.HtmlDecode(b.TextContent).Trim() == L[key].Value);

    private bool HasButton(IRenderedComponent<UserDetails> cut, string key) =>
        cut.FindAll("button").Any(b => WebUtility.HtmlDecode(b.TextContent).Trim() == L[key].Value);

    [Theory]
    [InlineData(false, "LockUser", "UnlockUser")]
    [InlineData(true, "UnlockUser", "LockUser")]
    public void LockButton_FollowsTheState(bool locked, string shown, string hidden)
    {
        _shown.Locked = locked;
        var cut = RenderUser();
        Assert.True(HasButton(cut, shown));
        Assert.False(HasButton(cut, hidden));
    }

    [Theory]
    [InlineData(false, "SuspendUser", "UnsuspendUser")]
    [InlineData(true, "UnsuspendUser", "SuspendUser")]
    public void SuspendButton_FollowsTheState(bool suspended, string shown, string hidden)
    {
        _shown.Suspended = suspended;
        var cut = RenderUser();
        Assert.True(HasButton(cut, shown));
        Assert.False(HasButton(cut, hidden));
    }

    [Theory]
    [InlineData(false, "MakeAdmin", "RemoveAdmin")]
    [InlineData(true, "RemoveAdmin", "MakeAdmin")]
    public void AdminButton_FollowsTheState(bool admin, string shown, string hidden)
    {
        _shown.Admin = admin;
        var cut = RenderUser();
        Assert.True(HasButton(cut, shown));
        Assert.False(HasButton(cut, hidden));
    }

    [Theory]
    [InlineData(false, "ShadowBanUser", "LiftShadowBan")]
    [InlineData(true, "LiftShadowBan", "ShadowBanUser")]
    public void ShadowBanButton_FollowsTheState(bool shadowBanned, string shown, string hidden)
    {
        _shown.ShadowBanned = shadowBanned;
        var cut = RenderUser();
        Assert.True(HasButton(cut, shown));
        Assert.False(HasButton(cut, hidden));
    }

    /// <summary>Each action: the handler, the state shown, the service method it calls and the value it sends.</summary>
    public static TheoryData<string, bool, string, bool, string> Actions => new()
    {
        { "ToggleLock", false, nameof(IUserService.SetUserLockedAsync), true, "LockUserConfirmation" },
        { "ToggleLock", true, nameof(IUserService.SetUserLockedAsync), false, "UnlockUserConfirmation" },
        { "ToggleSuspension", false, nameof(IUserService.SetUserSuspendedAsync), true, "SuspendUserConfirmation" },
        { "ToggleSuspension", true, nameof(IUserService.SetUserSuspendedAsync), false, "UnsuspendUserConfirmation" },
        { "ToggleAdmin", false, nameof(IUserService.SetUserAdminAsync), true, "MakeAdminConfirmation" },
        { "ToggleAdmin", true, nameof(IUserService.SetUserAdminAsync), false, "RemoveAdminConfirmation" },
        { "ToggleShadowBan", false, nameof(IUserService.SetUserShadowBannedAsync), true, "ShadowBanUserConfirmation" },
        { "ToggleShadowBan", true, nameof(IUserService.SetUserShadowBannedAsync), false, "LiftShadowBanConfirmation" },
    };

    private void SetState(string handler, bool state)
    {
        switch (handler)
        {
            case "ToggleLock": _shown.Locked = state; break;
            case "ToggleSuspension": _shown.Suspended = state; break;
            case "ToggleAdmin": _shown.Admin = state; break;
            case "ToggleShadowBan": _shown.ShadowBanned = state; break;
        }
    }

    [Theory, MemberData(nameof(Actions))]
    public async Task ConfirmedAction_TargetsTheDisplayedUser_ShowsTheResult_AndReloads(
        string handler, bool state, string serviceMethod, bool value, string confirmationKey)
    {
        SetState(handler, state);
        var cut = RenderUser();

        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Assert.Equal(L[confirmationKey, $"Alice Example ({UserA})"].Value, Confirmations[0].Message);
        Confirmations[0].Result.SetResult(true);
        await action;

        var call = Assert.Single(_users.CallsTo(serviceMethod));
        Assert.Equal((UserA, value), ((string)call[0]!, (bool)call[1]!));
        AssertSnackbars(_actionResult);
        Assert.Equal(2, DetailReads);
    }

    [Theory, MemberData(nameof(Actions))]
    public async Task CancelledConfirmation_SendsNothing(string handler, bool state, string serviceMethod, bool value, string confirmationKey)
    {
        _ = (value, confirmationKey);
        SetState(handler, state);
        var cut = RenderUser();

        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(false);
        await action;

        Assert.Empty(_users.CallsTo(serviceMethod));
        Assert.Equal(1, DetailReads);
    }

    [Theory, MemberData(nameof(Actions))]
    public async Task FailedAction_ShowsTheFailure_WithoutReloading(string handler, bool state, string serviceMethod, bool value, string confirmationKey)
    {
        _ = (serviceMethod, value, confirmationKey);
        _actionResult = OperationResult.Failure("Nope");
        SetState(handler, state);
        var cut = RenderUser();

        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;

        AssertSnackbars(_actionResult);
        Assert.Equal(1, DetailReads);
    }

    [Theory]
    [InlineData("ToggleLock", nameof(IUserService.SetUserLockedAsync))]
    [InlineData("ToggleSuspension", nameof(IUserService.SetUserSuspendedAsync))]
    [InlineData("ToggleAdmin", nameof(IUserService.SetUserAdminAsync))]
    [InlineData("ToggleShadowBan", nameof(IUserService.SetUserShadowBannedAsync))]
    public async Task NavigationDuringConfirmation_AbortsInsteadOfRetargeting(string handler, string serviceMethod)
    {
        var cut = RenderUser();

        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));
        Confirmations[0].Result.SetResult(true);
        await action;

        Assert.Empty(_users.CallsTo(serviceMethod));
        Assert.True(WarningShown());
    }

    // ---- The signed-in admin's own account ---------------------------------------------------------------

    [Fact]
    public void OwnAccount_RestrictingActionsAreDisabled_WithAHint()
    {
        SignInAs(UserA);
        _shown.Admin = true;
        var cut = RenderUser();

        foreach (var key in new[] { "LockUser", "SuspendUser", "RemoveAdmin", "ShadowBanUser", "DeactivateUser" })
        {
            Assert.True(Button(cut, key).HasAttribute("disabled"), key);
        }
        Assert.Contains(L["NotAvailableForOwnAccount"].Value, WebUtility.HtmlDecode(cut.Markup));
    }

    /// <summary>Lifting a restriction doesn't lock the admin out, so it stays available on their own account.</summary>
    [Fact]
    public void OwnAccount_LiftingActionsStayEnabled()
    {
        SignInAs(UserA);
        _shown.Locked = true;
        _shown.Suspended = true;
        _shown.ShadowBanned = true;
        var cut = RenderUser();

        foreach (var key in new[] { "UnlockUser", "UnsuspendUser", "LiftShadowBan" })
        {
            Assert.False(Button(cut, key).HasAttribute("disabled"), key);
        }
    }

    [Theory]
    [InlineData("ToggleLock")]
    [InlineData("ToggleSuspension")]
    [InlineData("ToggleAdmin")]
    [InlineData("ToggleShadowBan")]
    [InlineData("DeactivateUser")]
    public async Task OwnAccount_RestrictingHandler_AsksNothing_AndSendsNothing(string handler)
    {
        SignInAs(UserA);
        _shown.Admin = true;
        var cut = RenderUser();

        await InvokeHandler(cut, handler);

        Assert.Empty(Confirmations);
        Assert.DoesNotContain(_users.Calls, c => c.Method is nameof(IUserService.SetUserLockedAsync) or nameof(IUserService.SetUserSuspendedAsync)
            or nameof(IUserService.SetUserAdminAsync) or nameof(IUserService.SetUserShadowBannedAsync) or nameof(IUserService.DeactivateUserAsync));
    }

    [Fact]
    public void OtherAccount_ShowsNoHint_AndEnablesTheActions()
    {
        SignInAs(UserB);
        _shown.Admin = true;
        var cut = RenderUser();

        foreach (var key in new[] { "LockUser", "SuspendUser", "RemoveAdmin", "ShadowBanUser", "DeactivateUser" })
        {
            Assert.False(Button(cut, key).HasAttribute("disabled"), key);
        }
        Assert.DoesNotContain(L["NotAvailableForOwnAccount"].Value, WebUtility.HtmlDecode(cut.Markup));
    }
}
