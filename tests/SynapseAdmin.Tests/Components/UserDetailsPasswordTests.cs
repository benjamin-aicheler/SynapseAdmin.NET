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
/// Reset password and Reactivate on the user page (#584, AC-21): which of them the Actions card offers, what the page
/// passes to the dialogs, and that it sends the dialog's answer only for the user still shown.
/// </summary>
public class UserDetailsPasswordTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";

    private readonly Stub _users;
    private UserDetailViewModel _shown = new() { UserId = UserA, DisplayName = "Alice Example" };
    private OperationResult _actionResult = OperationResult.Ok("Done");

    public UserDetailsPasswordTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), args => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                (string)args[0]! == UserA ? _shown : new UserDetailViewModel { UserId = UserB, DisplayName = "Bob Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())))
            .On(nameof(IUserService.ResetPasswordAsync), _ => Task.FromResult(_actionResult))
            .On(nameof(IUserService.ReactivateUserAsync), _ => Task.FromResult(_actionResult));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private int DetailReads => _users.CallsTo(nameof(IUserService.GetUserDetailsAsync)).Count;

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

    /// <summary>Every dialog shown closes with <paramref name="data"/>, once <paramref name="closed"/> completes.</summary>
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

    private AngleSharp.Dom.IElement? Button(IRenderedComponent<UserDetails> cut, string key) =>
        cut.FindAll("button").SingleOrDefault(b => WebUtility.HtmlDecode(b.TextContent).Trim() == L[key].Value);

    /// <summary>The parameters of the single dialog shown, which the stub knows by its title (it records no type argument).</summary>
    private DialogParameters ShownParameters(string dialogType)
    {
        var call = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
        Assert.Equal(L[dialogType == nameof(ResetPasswordDialog) ? "ResetPassword" : "ReactivateUser"].Value, call[0]);
        return (DialogParameters)call[1]!;
    }

    [Fact]
    public void ActiveAccount_OffersResetPasswordAndDeactivate_NotReactivate()
    {
        var cut = RenderUser();

        Assert.False(Button(cut, "ResetPassword")!.HasAttribute("disabled"));
        Assert.NotNull(Button(cut, "DeactivateUser"));
        Assert.Null(Button(cut, "ReactivateUser"));
    }

    /// <summary>A deactivated account gets its password from Reactivate; Tuwunel's reset would also reactivate it.</summary>
    [Fact]
    public async Task DeactivatedAccount_OffersReactivate_AndDisablesResetPassword()
    {
        _shown.Deactivated = true;
        var cut = RenderUser();

        Assert.True(Button(cut, "ResetPassword")!.HasAttribute("disabled"));
        Assert.NotNull(Button(cut, "ReactivateUser"));
        Assert.Null(Button(cut, "DeactivateUser"));

        await InvokeHandler(cut, "OpenResetPasswordDialog");
        Assert.Empty(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetPassword_SendsTheDialogsAnswer_ForTheShownUser(bool logoutDevices)
    {
        DialogsReturn(new UserPasswordResetViewModel { Password = "new-secret", LogoutDevices = logoutDevices });
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenResetPasswordDialog");

        var parameters = ShownParameters(nameof(ResetPasswordDialog));
        Assert.Equal($"Alice Example ({UserA})", parameters[nameof(ResetPasswordDialog.Target)]);
        Assert.Equal(false, parameters[nameof(ResetPasswordDialog.IsOwnAccount)]);
        var call = Assert.Single(_users.CallsTo(nameof(IUserService.ResetPasswordAsync)));
        Assert.Equal((UserA, "new-secret", logoutDevices), ((string)call[0]!, (string)call[1]!, (bool)call[2]!));
        AssertSnackbars(_actionResult);
        // A reset changes nothing the page shows.
        Assert.Equal(1, DetailReads);
    }

    [Fact]
    public async Task ResetPassword_OwnAccount_TellsTheDialog()
    {
        SignInAs(UserA);
        DialogsReturn(new UserPasswordResetViewModel { Password = "new-secret" });
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenResetPasswordDialog");

        Assert.Equal(true, ShownParameters(nameof(ResetPasswordDialog))[nameof(ResetPasswordDialog.IsOwnAccount)]);
        Assert.Single(_users.CallsTo(nameof(IUserService.ResetPasswordAsync)));
    }

    [Fact]
    public async Task ResetPassword_Cancelled_SendsNothing()
    {
        CancelDialogs();
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenResetPasswordDialog");

        Assert.Empty(_users.CallsTo(nameof(IUserService.ResetPasswordAsync)));
        AssertSnackbars();
    }

    [Theory]
    [InlineData("OpenResetPasswordDialog", false, nameof(IUserService.ResetPasswordAsync))]
    [InlineData("OpenReactivateDialog", true, nameof(IUserService.ReactivateUserAsync))]
    public async Task NavigationWhileTheDialogIsOpen_AbortsInsteadOfRetargeting(string handler, bool deactivated, string serviceMethod)
    {
        _shown.Deactivated = deactivated;
        var closed = new TaskCompletionSource();
        DialogsReturn(handler == "OpenResetPasswordDialog"
            ? new UserPasswordResetViewModel { Password = "new-secret" }
            : new UserReactivateViewModel { Password = "new-secret" }, closed.Task);
        var cut = RenderUser();

        var action = InvokeHandler(cut, handler);
        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));
        closed.SetResult();
        await action;

        Assert.Empty(_users.CallsTo(serviceMethod));
        Assert.True(WarningShown());
    }

    [Theory]
    [InlineData("new-secret")]
    [InlineData(null)]
    public async Task Reactivate_SendsTheDialogsPassword_AndReloads(string? password)
    {
        _shown.Deactivated = true;
        DialogsReturn(new UserReactivateViewModel { Password = password });
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenReactivateDialog");

        Assert.Equal($"Alice Example ({UserA})", ShownParameters(nameof(ReactivateUserDialog))[nameof(ReactivateUserDialog.Target)]);
        var call = Assert.Single(_users.CallsTo(nameof(IUserService.ReactivateUserAsync)));
        Assert.Equal((UserA, password), ((string)call[0]!, (string?)call[1]));
        AssertSnackbars(_actionResult);
        Assert.Equal(2, DetailReads);
    }

    [Fact]
    public async Task Reactivate_Failed_ShowsTheFailure_WithoutReloading()
    {
        _shown.Deactivated = true;
        _actionResult = OperationResult.Failure("Nope");
        DialogsReturn(new UserReactivateViewModel());
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenReactivateDialog");

        AssertSnackbars(_actionResult);
        Assert.Equal(1, DetailReads);
    }

    [Fact]
    public async Task Reactivate_ActiveAccount_OpensNoDialog()
    {
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenReactivateDialog");

        Assert.Empty(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
    }
}
