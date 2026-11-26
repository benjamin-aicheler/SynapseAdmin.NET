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
/// The Edit profile dialog (#584, AC-22): it shows the current display name, offers "Remove avatar" only for a user
/// with one, enables Save only after a change, and returns only what changed.
/// </summary>
public class UserProfileDialogTests : DetailPageTestBase
{
    public UserProfileDialogTests()
    {
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private async Task<(IRenderedComponent<UserProfileDialog> Dialog, IDialogReference Reference)> ShowAsync(
        string? displayName = "Alice Example", bool hasAvatar = true)
    {
        var provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        var parameters = new DialogParameters
        {
            [nameof(UserProfileDialog.Target)] = "Alice Example (@alice:hs)",
            [nameof(UserProfileDialog.CurrentDisplayName)] = displayName,
            [nameof(UserProfileDialog.HasAvatar)] = hasAvatar,
        };
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>()
            .ShowAsync<UserProfileDialog>("Dialog", parameters));
        var dialog = provider.FindComponent<UserProfileDialog>();
        dialog.WaitForElement("input");
        return (dialog, reference);
    }

    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<UserProfileDialog> dialog) =>
        dialog.FindAll(".mud-dialog-actions button")[^1];

    private static AngleSharp.Dom.IElement NameInput(IRenderedComponent<UserProfileDialog> dialog) =>
        dialog.Find("input:not([type=checkbox])");

    private static async Task<UserProfileEditViewModel> SaveAsync(IRenderedComponent<UserProfileDialog> dialog, IDialogReference reference)
    {
        dialog.WaitForAssertion(() => Assert.False(SaveButton(dialog).HasAttribute("disabled")));
        SaveButton(dialog).Click();
        var result = await reference.Result;
        Assert.False(result!.Canceled);
        return Assert.IsType<UserProfileEditViewModel>(result.Data);
    }

    [Fact]
    public async Task ShowsTheUserAndTheCurrentName_WithSaveDisabled()
    {
        var (dialog, _) = await ShowAsync();

        Assert.Contains(L["EditProfileFor", "Alice Example (@alice:hs)"].Value, WebUtility.HtmlDecode(dialog.Markup));
        Assert.Equal("Alice Example", NameInput(dialog).GetAttribute("value"));
        Assert.True(SaveButton(dialog).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ChangedName_ReturnsOnlyTheName()
    {
        var (dialog, reference) = await ShowAsync();

        NameInput(dialog).Input("Alice B. Example");

        var changes = await SaveAsync(dialog, reference);
        Assert.Equal(("Alice B. Example", false), (changes.DisplayName, changes.RemoveAvatar));
    }

    [Fact]
    public async Task EmptiedName_ReturnsAnEmptyName_WhichRemovesIt()
    {
        var (dialog, reference) = await ShowAsync();

        NameInput(dialog).Input("");

        Assert.Equal("", (await SaveAsync(dialog, reference)).DisplayName);
    }

    [Fact]
    public async Task RemoveAvatar_ReturnsOnlyTheRemoval()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find("input[type=checkbox]").Change(true);

        var changes = await SaveAsync(dialog, reference);
        Assert.Equal(((string?)null, true), (changes.DisplayName, changes.RemoveAvatar));
    }

    [Fact]
    public async Task UserWithoutAnAvatar_HasNoRemoveAvatarOption()
    {
        var (dialog, _) = await ShowAsync(hasAvatar: false);

        Assert.Empty(dialog.FindAll("input[type=checkbox]"));
        Assert.DoesNotContain(L["RemoveAvatar"].Value, WebUtility.HtmlDecode(dialog.Markup));
    }

    /// <summary>Typing the name back, or leaving a missing name empty, is no change.</summary>
    [Theory]
    [InlineData("Alice Example", "Alice Example")]
    [InlineData(null, "")]
    public async Task UnchangedName_KeepsSaveDisabled(string? current, string typed)
    {
        var (dialog, _) = await ShowAsync(current);

        NameInput(dialog).Input("changed");
        dialog.WaitForAssertion(() => Assert.False(SaveButton(dialog).HasAttribute("disabled")));
        NameInput(dialog).Input(typed);

        dialog.WaitForAssertion(() => Assert.True(SaveButton(dialog).HasAttribute("disabled")));
    }
}
