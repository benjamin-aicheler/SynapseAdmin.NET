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
/// The user page's join dialog (#605, AC-27): it names the user and the conditions, takes a typed room ID or a pick from
/// the user's <c>leave</c>, <c>invite</c> and <c>knock</c> memberships, refuses a malformed ID or alias and a room the user
/// is joined to or banned from, and returns the trimmed room ID.
/// </summary>
public class UserJoinRoomDialogTests : DetailPageTestBase
{
    private const string User = "@alice:hs";

    private static readonly UserMembershipViewModel[] Memberships =
    [
        new() { RoomId = "!joined:hs", Membership = "join" },
        new() { RoomId = "!left:hs", Membership = "leave" },
        new() { RoomId = "!invited:hs", Membership = "invite" },
        new() { RoomId = "!knocked:hs", Membership = "knock" },
        new() { RoomId = "!banned:hs", Membership = "ban" },
    ];

    public UserJoinRoomDialogTests()
    {
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private sealed record Shown(
        IRenderedComponent<MudDialogProvider> Provider,
        IRenderedComponent<UserJoinRoomDialog> Dialog,
        IDialogReference Reference);

    private async Task<Shown> ShowAsync(params UserMembershipViewModel[] memberships)
    {
        var provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        var parameters = new DialogParameters
        {
            [nameof(UserJoinRoomDialog.UserLabel)] = User,
            [nameof(UserJoinRoomDialog.Memberships)] = (IReadOnlyList<UserMembershipViewModel>)memberships,
        };
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>()
            .ShowAsync<UserJoinRoomDialog>("Dialog", parameters));
        var dialog = provider.FindComponent<UserJoinRoomDialog>();
        dialog.WaitForElement("input");
        return new Shown(provider, dialog, reference);
    }

    private static AngleSharp.Dom.IElement SubmitButton(Shown shown) =>
        shown.Dialog.FindAll(".mud-dialog-actions button")[^1];

    private static string Text(IRenderedComponent<MudDialogProvider> provider) => WebUtility.HtmlDecode(provider.Markup);

    private static string[] PickTexts(Shown shown) =>
        shown.Dialog.FindAll(".join-room-pick").Select(c => WebUtility.HtmlDecode(c.TextContent).Trim()).ToArray();

    private static async Task<string> SubmitAsync(Shown shown)
    {
        shown.Dialog.WaitForAssertion(() => Assert.False(SubmitButton(shown).HasAttribute("disabled")));
        SubmitButton(shown).Click();
        var result = await shown.Reference.Result.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result!.Canceled);
        return Assert.IsType<string>(result.Data);
    }

    [Fact]
    public async Task NamesTheUser_AndTheReferencesConditions()
    {
        var shown = await ShowAsync(Memberships);

        var text = Text(shown.Provider);
        Assert.Contains(L["JoinToRoomTitle", User].Value, text);
        Assert.Contains(L["JoinToRoomDescription", User].Value, text);
        Assert.Contains(L["JoinToRoomRoomIdHint"].Value, text);
        Assert.Equal(L["JoinToRoom"].Value, SubmitButton(shown).TextContent.Trim());
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    /// <summary>Only rooms the user can be invited to again are offered: left, invited and knocked, in the page's order.</summary>
    [Fact]
    public async Task Picks_AreTheLeftInvitedAndKnockedRooms()
    {
        var shown = await ShowAsync(Memberships);

        Assert.Contains(L["JoinToRoomPicks"].Value, Text(shown.Provider));
        Assert.Equal(["!left:hs · leave", "!invited:hs · invite", "!knocked:hs · knock"], PickTexts(shown));
    }

    [Fact]
    public async Task WithoutPickableRooms_ShowsNoPicks()
    {
        var shown = await ShowAsync(Memberships[0], Memberships[4]);

        Assert.Empty(PickTexts(shown));
        Assert.DoesNotContain(L["JoinToRoomPicks"].Value, Text(shown.Provider));
    }

    [Fact]
    public async Task Pick_FillsTheField_AndIsReturned()
    {
        var shown = await ShowAsync(Memberships);

        shown.Dialog.FindAll(".join-room-pick")[0].Click();

        shown.Dialog.WaitForAssertion(() => Assert.Equal("!left:hs", shown.Dialog.Find("input").GetAttribute("value")));
        Assert.Equal("!left:hs", await SubmitAsync(shown));
    }

    /// <summary>Both room ID forms are accepted, the one of room version 12 without a domain included.</summary>
    [Theory]
    [InlineData("  !other:hs ", "!other:hs")]
    [InlineData("!QxR7a-Kq_9abc", "!QxR7a-Kq_9abc")]
    public async Task TypedRoomId_IsReturnedTrimmed(string typed, string expected)
    {
        var shown = await ShowAsync(Memberships);

        shown.Dialog.Find("input").Input(typed);

        Assert.Equal(expected, await SubmitAsync(shown));
    }

    [Theory]
    [InlineData("#room:hs")]
    [InlineData("@bob:hs")]
    [InlineData("!")]
    [InlineData("!ro om:hs")]
    public async Task NoRoomId_IsRefused(string typed)
    {
        var shown = await ShowAsync(Memberships);

        shown.Dialog.Find("input").Input(typed);

        shown.Dialog.WaitForAssertion(() => Assert.Contains(L["InvalidRoomId"].Value, Text(shown.Provider)));
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    /// <summary>Synapse invites before it joins, and the authorization rules refuse an invite to a joined or banned user.</summary>
    [Theory]
    [InlineData("!joined:hs", "UserAlreadyInRoom")]
    [InlineData(" !banned:hs", "UserBannedFromRoom")]
    public async Task JoinedOrBannedRoom_IsRefused(string typed, string key)
    {
        var shown = await ShowAsync(Memberships);

        shown.Dialog.Find("input").Input(typed);

        shown.Dialog.WaitForAssertion(() => Assert.Contains(L[key].Value, Text(shown.Provider)));
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Blank_CantBeSubmitted_WithoutAnError()
    {
        var shown = await ShowAsync(Memberships);

        shown.Dialog.Find("input").Input("   ");

        shown.Dialog.WaitForAssertion(() => Assert.True(SubmitButton(shown).HasAttribute("disabled")));
        Assert.DoesNotContain(L["InvalidRoomId"].Value, Text(shown.Provider));
    }

    [Fact]
    public async Task Cancel_ClosesAsCancelled()
    {
        var shown = await ShowAsync(Memberships);

        shown.Dialog.FindAll(".mud-dialog-actions button")[0].Click();

        var result = await shown.Reference.Result.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result!.Canceled);
    }
}
