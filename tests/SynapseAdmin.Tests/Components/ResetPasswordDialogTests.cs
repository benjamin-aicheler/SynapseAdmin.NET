using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The Reset password dialog (#584, AC-21): a new password of at least 8 characters, "Log out all devices" off by
/// default, and on the signed-in admin's own account the note that this session stays signed in.
/// </summary>
public class ResetPasswordDialogTests : DetailPageTestBase
{
    public ResetPasswordDialogTests()
    {
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private async Task<(IRenderedComponent<ResetPasswordDialog> Dialog, IDialogReference Reference)> ShowAsync(bool isOwnAccount = false)
    {
        var provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        var parameters = new DialogParameters
        {
            [nameof(ResetPasswordDialog.Target)] = "Alice Example (@alice:hs)",
            [nameof(ResetPasswordDialog.IsOwnAccount)] = isOwnAccount,
        };
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>()
            .ShowAsync<ResetPasswordDialog>("Dialog", parameters));
        var dialog = provider.FindComponent<ResetPasswordDialog>();
        dialog.WaitForElement("input[type=password]");
        return (dialog, reference);
    }

    private static void Submit(IRenderedComponent<ResetPasswordDialog> dialog) =>
        dialog.FindAll(".mud-dialog-actions button")[^1].Click();

    /// <summary>
    /// The form keeps the button disabled while the required password is missing, and enables it after the input is
    /// validated, which happens after the next render: wait for it, as a user would.
    /// </summary>
    private static void SubmitOnceEnabled(IRenderedComponent<ResetPasswordDialog> dialog)
    {
        dialog.WaitForAssertion(() => Assert.False(dialog.FindAll(".mud-dialog-actions button")[^1].HasAttribute("disabled")));
        Submit(dialog);
    }

    private static string Text(IRenderedComponent<ResetPasswordDialog> dialog) => WebUtility.HtmlDecode(dialog.Markup);

    [Fact]
    public async Task ShowsTheUser_AndLogsNothingOutByDefault()
    {
        var (dialog, reference) = await ShowAsync();
        Assert.Contains(L["ResetPasswordFor", "Alice Example (@alice:hs)"].Value, Text(dialog));

        dialog.Find("input[type=password]").Input("new-secret");
        SubmitOnceEnabled(dialog);

        var result = await reference.Result;
        Assert.False(result!.Canceled);
        var model = Assert.IsType<UserPasswordResetViewModel>(result.Data);
        Assert.Equal(("new-secret", false), (model.Password, model.LogoutDevices));
    }

    [Fact]
    public async Task LogOutAllDevices_IsReturnedWhenTurnedOn()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find("input[type=password]").Input("new-secret");
        dialog.Find("input[type=checkbox]").Change(true);
        SubmitOnceEnabled(dialog);

        Assert.True(Assert.IsType<UserPasswordResetViewModel>((await reference.Result)!.Data).LogoutDevices);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public async Task MissingOrShortPassword_KeepsTheDialogOpen(string password)
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find("input[type=password]").Input(password);
        Submit(dialog);

        Assert.False(reference.Result.IsCompleted);
    }

    /// <summary>After a rejected password, a valid one enables the button again and submits.</summary>
    [Fact]
    public async Task CorrectedPassword_SubmitsAfterARejectedOne()
    {
        var (dialog, reference) = await ShowAsync();
        dialog.Find("input[type=password]").Input("short");
        Submit(dialog);
        Assert.False(reference.Result.IsCompleted);

        dialog.Find("input[type=password]").Input("new-secret");
        SubmitOnceEnabled(dialog);

        Assert.Equal("new-secret", Assert.IsType<UserPasswordResetViewModel>((await reference.Result)!.Data).Password);
    }

    [Theory]
    [InlineData(false, "LogOutAllDevicesHelper", "LogOutAllDevicesOwnAccountHelper")]
    [InlineData(true, "LogOutAllDevicesOwnAccountHelper", "LogOutAllDevicesHelper")]
    public async Task ExplainsLoggingOut_ForTheAccountShown(bool isOwnAccount, string shown, string hidden)
    {
        var (dialog, _) = await ShowAsync(isOwnAccount);

        Assert.Contains(L[shown].Value, Text(dialog));
        Assert.DoesNotContain(L[hidden].Value, Text(dialog));
    }

    [Fact]
    public async Task Cancel_ReturnsNothing()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.FindAll(".mud-dialog-actions button")[0].Click();

        Assert.True((await reference.Result)!.Canceled);
    }
}
