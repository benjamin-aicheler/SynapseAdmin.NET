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
/// Which results the user page reports in a snackbar: every result except a silent cancellation, also a user load, a
/// "login as user" or a media page that succeeded without data.
/// </summary>
public class UserDetailsSnackbarTests : DetailPageTestBase
{
    private const string UserId = "@alice:hs";

    private readonly Stub _users;
    private OperationResult _result = new();
    private OperationResult<UserDetailViewModel> _user = OperationResult<UserDetailViewModel>.Ok(
        new UserDetailViewModel { UserId = UserId, DisplayName = "Alice Example" });
    private OperationResult<string> _login = new();
    private OperationResult<UserMediaPageViewModel> _page = OperationResult<UserMediaPageViewModel>.Ok(
        new UserMediaPageViewModel { TotalCount = 1, Items = [new() { MediaId = "upload1" }] });

    public UserDetailsSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), _ => Task.FromResult(_user))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(_page))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel { Count = 1 })))
            .On(nameof(IUserService.LoginAsUserAsync), _ => Task.FromResult(_login))
            .On(nameof(IUserService.DeactivateUserAsync), _ => Task.FromResult(_result))
            .On(nameof(IUserService.QuarantineMediaAsync), _ => Task.FromResult(_result))
            .On(nameof(IUserService.SetUserExperimentalFeaturesAsync), _ => Task.FromResult(_result));
        Services.AddSingleton(Stub.Create<IMediaService>(s =>
        {
            foreach (var method in typeof(IMediaService).GetMethods().Where(m => m.ReturnType == typeof(Task<OperationResult>)))
            {
                s.On(method.Name, _ => Task.FromResult(_result));
            }
        }));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IRenderedComponent<UserDetails> RenderUser()
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, UserId));
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));
        return cut;
    }

    [Fact]
    public async Task RenewAccountDialog_IsSmall_AndClosesOnEscape()
    {
        CancelDialogs();
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenRenewAccountDialog");

        AssertDialogOptions(new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
    }

    public static TheoryData<string, string> Actions()
    {
        var data = new TheoryData<string, string>();
        foreach (var handler in new[] { "DeactivateUser", "QuarantineAllMedia", "QuarantineSingleMedia", "UnquarantineSingleMedia", "DeleteSingleMedia", "ToggleMediaProtection", "ToggleExperimentalFeature" })
        {
            foreach (var kind in new[] { "success", "failure", "cancelled" }) data.Add(handler, kind);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Action_ReportsEveryResultExceptACancellation(string handler, string kind)
    {
        _result = Make<OperationResult>(kind);
        var cut = RenderUser();
        object?[] args = handler switch
        {
            "ToggleMediaProtection" => [new UserMediaItemViewModel { MediaId = "upload1" }],
            "ToggleExperimentalFeature" => [new UserExperimentalFeatureViewModel { FeatureKey = "msc3881" }, true],
            "QuarantineSingleMedia" or "UnquarantineSingleMedia" or "DeleteSingleMedia" => ["upload1"],
            _ => []
        };

        var action = InvokeHandler(cut, handler, args);
        if (handler is not ("ToggleMediaProtection" or "ToggleExperimentalFeature"))
        {
            cut.WaitForAssertion(() => Assert.Single(Confirmations));
            Confirmations[0].Result.SetResult(true);
        }
        await action;

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }

    /// <summary>A reported user-load result also replaces the page's "user not found" text, even a success without a user.</summary>
    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", true)]
    public void UserLoad_ReportsEveryResultExceptACancellation(string kind, bool shown)
    {
        _user = Make<OperationResult<UserDetailViewModel>>(kind);

        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, UserId));

        cut.WaitForAssertion(() => Assert.Contains(shown ? _user.Message : L["UserNotFound"].Value, WebUtility.HtmlDecode(cut.Markup)));
        AssertSnackbars(shown ? [_user] : []);
    }

    /// <summary>Without a token, the login result is reported (unless cancelled) and no token dialog follows.</summary>
    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", true)]
    public async Task LoginAsUser_ReportsEveryResultWithoutATokenExceptACancellation(string kind, bool shown)
    {
        _login = Make<OperationResult<string>>(kind);
        var cut = RenderUser();

        await InvokeHandler(cut, "LoginAsUser");

        AssertSnackbars(shown ? [_login] : []);
        Assert.Empty(Confirmations);
    }

    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", true)]
    public void MediaPageLoad_ReportsEveryResultWithoutAPageExceptACancellation(string kind, bool shown)
    {
        _page = Make<OperationResult<UserMediaPageViewModel>>(kind);
        var cut = RenderUser();

        cut.FindAll(".mud-tab").First(t => t.TextContent.Trim() == L["Media"].Value).Click();

        cut.WaitForAssertion(() => Assert.Single(_users.CallsTo(nameof(IUserService.GetUserMediaPageAsync))));
        AssertSnackbars(shown ? [_page] : []);
    }
}
