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
/// Edit profile on the user page (#584, AC-22): what the page passes to the dialog, and that it sends the dialog's
/// changes only for the user still shown, then reloads the user.
/// </summary>
public class UserDetailsProfileTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";

    private readonly Stub _users;
    private readonly UserDetailViewModel _shown = new() { UserId = UserA, DisplayName = "Alice Example", AvatarUrl = "mxc://hs/avatar" };
    private OperationResult _actionResult = OperationResult.Ok("Done");

    public UserDetailsProfileTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), args => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                (string)args[0]! == UserA ? _shown : new UserDetailViewModel { UserId = UserB, DisplayName = "Bob Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())))
            .On(nameof(IUserService.UpdateUserProfileAsync), _ => Task.FromResult(_actionResult));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private int DetailReads => _users.CallsTo(nameof(IUserService.GetUserDetailsAsync)).Count;

    private void DialogsReturn(object data, Task? closed = null) =>
        DialogStub.On(nameof(IDialogService.ShowAsync), _ => Task.FromResult(Stub.Create<IDialogReference>(s => s.On("get_Result",
            _ => CloseWith(data, closed)))));

    private static async Task<DialogResult?> CloseWith(object data, Task? closed)
    {
        if (closed != null) await closed;
        return DialogResult.Ok(data);
    }

    private IRenderedComponent<UserDetails> RenderUser()
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, UserA));
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));
        return cut;
    }

    [Fact]
    public async Task EditProfile_PassesTheCurrentProfile_SendsTheChanges_AndReloads()
    {
        var changes = new UserProfileEditViewModel { DisplayName = "Alice B. Example", RemoveAvatar = true };
        DialogsReturn(changes);
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenProfileDialog");

        var shown = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
        Assert.Equal(L["EditProfile"].Value, shown[0]);
        var parameters = (DialogParameters)shown[1]!;
        Assert.Equal($"Alice Example ({UserA})", parameters[nameof(UserProfileDialog.Target)]);
        Assert.Equal("Alice Example", parameters[nameof(UserProfileDialog.CurrentDisplayName)]);
        Assert.Equal(true, parameters[nameof(UserProfileDialog.HasAvatar)]);
        var call = Assert.Single(_users.CallsTo(nameof(IUserService.UpdateUserProfileAsync)));
        Assert.Equal(UserA, call[0]);
        Assert.Same(changes, call[1]);
        AssertSnackbars(_actionResult);
        Assert.Equal(2, DetailReads);
    }

    [Fact]
    public async Task EditProfile_Failed_ShowsTheFailure_WithoutReloading()
    {
        _actionResult = OperationResult.Failure("Nope");
        DialogsReturn(new UserProfileEditViewModel { RemoveAvatar = true });
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenProfileDialog");

        AssertSnackbars(_actionResult);
        Assert.Equal(1, DetailReads);
    }

    [Fact]
    public async Task EditProfile_Cancelled_SendsNothing()
    {
        CancelDialogs();
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenProfileDialog");

        Assert.Empty(_users.CallsTo(nameof(IUserService.UpdateUserProfileAsync)));
        AssertSnackbars();
    }

    [Fact]
    public async Task EditProfile_NavigationWhileTheDialogIsOpen_AbortsInsteadOfRetargeting()
    {
        var closed = new TaskCompletionSource();
        DialogsReturn(new UserProfileEditViewModel { DisplayName = "Alice B. Example" }, closed.Task);
        var cut = RenderUser();

        var action = InvokeHandler(cut, "OpenProfileDialog");
        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));
        closed.SetResult();
        await action;

        Assert.Empty(_users.CallsTo(nameof(IUserService.UpdateUserProfileAsync)));
        Assert.True(WarningShown());
    }
}
