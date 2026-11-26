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
/// The Reactivate dialog (#584, AC-21): the new password is optional, at least 8 characters when given, and the dialog
/// says what reactivating without one means.
/// </summary>
public class ReactivateUserDialogTests : DetailPageTestBase
{
    public ReactivateUserDialogTests()
    {
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private async Task<(IRenderedComponent<ReactivateUserDialog> Dialog, IDialogReference Reference)> ShowAsync()
    {
        var provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        var parameters = new DialogParameters { [nameof(ReactivateUserDialog.Target)] = "Alice Example (@alice:hs)" };
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>()
            .ShowAsync<ReactivateUserDialog>("Dialog", parameters));
        var dialog = provider.FindComponent<ReactivateUserDialog>();
        dialog.WaitForElement("input[type=password]");
        return (dialog, reference);
    }

    private static void Submit(IRenderedComponent<ReactivateUserDialog> dialog) =>
        dialog.FindAll(".mud-dialog-actions button")[^1].Click();

    [Fact]
    public async Task ShowsTheUser_AndWhatHappensWithoutAPassword()
    {
        var (dialog, _) = await ShowAsync();

        var text = WebUtility.HtmlDecode(dialog.Markup);
        Assert.Contains(L["ReactivateUserFor", "Alice Example (@alice:hs)"].Value, text);
        Assert.Contains(L["ReactivationPasswordHelper"].Value, text);
    }

    [Fact]
    public async Task WithoutAPassword_ReturnsNone()
    {
        var (dialog, reference) = await ShowAsync();

        Submit(dialog);

        var result = await reference.Result;
        Assert.False(result!.Canceled);
        Assert.True(string.IsNullOrEmpty(Assert.IsType<UserReactivateViewModel>(result.Data).Password));
    }

    [Fact]
    public async Task WithAPassword_ReturnsIt()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find("input[type=password]").Input("new-secret");
        Submit(dialog);

        Assert.Equal("new-secret", Assert.IsType<UserReactivateViewModel>((await reference.Result)!.Data).Password);
    }

    [Fact]
    public async Task ShortPassword_KeepsTheDialogOpen()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find("input[type=password]").Input("short");
        Submit(dialog);

        Assert.False(reference.Result.IsCompleted);
    }
}
