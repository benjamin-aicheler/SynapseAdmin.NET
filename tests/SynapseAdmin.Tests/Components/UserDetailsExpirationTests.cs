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
/// Synapse's user details have no <c>expiration_ts</c>, so the page knows an account's expiration only from a renewal
/// it made itself. Until then the row is hidden and the renewal dialog starts without one (#546).
/// </summary>
public class UserDetailsExpirationTests : DetailPageTestBase
{
    private const string UserId = "@alice:hs";
    private const long RenewedUntil = 1_785_542_400_000;

    public UserDetailsExpirationTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.GetUserDetailsAsync), _ => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                new UserDetailViewModel { UserId = UserId, DisplayName = "Alice Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IRenderedComponent<UserDetails> RenderUser()
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, UserId));
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));
        return cut;
    }

    [Fact]
    public async Task UnknownExpiration_HidesTheRow_AndTheDialogStartsWithoutOne()
    {
        CancelDialogs();
        var cut = RenderUser();

        Assert.DoesNotContain(L["AccountExpiration"].Value, System.Net.WebUtility.HtmlDecode(cut.Markup));
        await InvokeHandler(cut, "OpenRenewAccountDialog");

        var parameters = (DialogParameters)Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)))[1]!;
        Assert.Null(parameters[nameof(RenewAccountDialog.CurrentExpirationTimestamp)]);
    }

    [Fact]
    public async Task Renewal_ShowsTheNewExpiration()
    {
        DialogStub.On(nameof(IDialogService.ShowAsync), _ => Task.FromResult(
            Stub.Create<IDialogReference>(s => s.On("get_Result", _ => Task.FromResult<DialogResult?>(DialogResult.Ok(RenewedUntil))))));
        var cut = RenderUser();

        await InvokeHandler(cut, "OpenRenewAccountDialog");

        cut.WaitForAssertion(() => Assert.Contains(L["AccountExpiration"].Value, System.Net.WebUtility.HtmlDecode(cut.Markup)));
    }
}
