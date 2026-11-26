using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The Rename device dialog (#598, AC-23): prefilled with the current name, which is required and has at most 100
/// characters, and Rename is enabled only for a changed, valid name.
/// </summary>
public class RenameDeviceDialogTests : DetailPageTestBase
{
    private const string Target = "Alice Example (@alice:hs)";

    public RenameDeviceDialogTests()
    {
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private async Task<(IRenderedComponent<RenameDeviceDialog> Dialog, IDialogReference Reference)> ShowAsync(string? currentName = "Phone")
    {
        var provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        var parameters = new DialogParameters
        {
            [nameof(RenameDeviceDialog.Target)] = Target,
            [nameof(RenameDeviceDialog.DeviceId)] = "QBUAZIFURK",
            [nameof(RenameDeviceDialog.CurrentName)] = currentName,
        };
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>()
            .ShowAsync<RenameDeviceDialog>("Dialog", parameters));
        var dialog = provider.FindComponent<RenameDeviceDialog>();
        dialog.WaitForElement("input");
        return (dialog, reference);
    }

    private static AngleSharp.Dom.IElement RenameButton(IRenderedComponent<RenameDeviceDialog> dialog) =>
        dialog.FindAll(".mud-dialog-actions button")[^1];

    private static string Text(IRenderedComponent<RenameDeviceDialog> dialog) => WebUtility.HtmlDecode(dialog.Markup);

    [Fact]
    public async Task ShowsTheDeviceAndUser_PrefilledWithTheCurrentName()
    {
        var (dialog, _) = await ShowAsync();

        Assert.Contains(L["RenameDeviceFor", "QBUAZIFURK", Target].Value, Text(dialog));
        Assert.Equal("Phone", dialog.Find("input").GetAttribute("value"));
        Assert.True(RenameButton(dialog).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ChangedName_IsReturned()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find("input").Input("Work phone");
        dialog.WaitForAssertion(() => Assert.False(RenameButton(dialog).HasAttribute("disabled")));
        RenameButton(dialog).Click();

        var result = await reference.Result;
        Assert.False(result!.Canceled);
        Assert.Equal("Work phone", result.Data);
    }

    [Fact]
    public async Task DeviceWithoutAName_StartsEmpty_AndNeedsOne()
    {
        var (dialog, _) = await ShowAsync(currentName: null);

        Assert.Equal("", dialog.Find("input").GetAttribute("value") ?? "");
        Assert.True(RenameButton(dialog).HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("   ", "DeviceNameRequired")]
    [InlineData("", "DeviceNameRequired")]
    public async Task EmptyName_IsRefused(string name, string errorKey)
    {
        var (dialog, _) = await ShowAsync();

        dialog.Find("input").Input(name);

        dialog.WaitForAssertion(() => Assert.Contains(L[errorKey].Value, Text(dialog)));
        Assert.True(RenameButton(dialog).HasAttribute("disabled"));
    }

    [Fact]
    public async Task NameOver100Characters_IsRefused_100AreAllowed()
    {
        var (dialog, _) = await ShowAsync();

        dialog.Find("input").Input(new string('a', 101));
        dialog.WaitForAssertion(() => Assert.Contains(L["DeviceNameTooLong", 100].Value, Text(dialog)));
        Assert.True(RenameButton(dialog).HasAttribute("disabled"));

        dialog.Find("input").Input(new string('a', 100));
        dialog.WaitForAssertion(() => Assert.False(RenameButton(dialog).HasAttribute("disabled")));
    }

    [Fact]
    public async Task Cancel_ClosesAsCancelled()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.FindAll(".mud-dialog-actions button")[0].Click();

        Assert.True((await reference.Result)!.Canceled);
    }
}
