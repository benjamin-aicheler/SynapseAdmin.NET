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
/// The user page's join dialog (#605, AC-27; #606, AC-28): it names the user and the conditions, takes a typed room ID or
/// alias or a pick from the user's <c>leave</c>, <c>invite</c> and <c>knock</c> memberships, and servers to join through
/// for a room ID. It refuses a malformed room, an invalid server, and a room ID the user is joined to or banned from, and
/// returns the trimmed room and the servers as a <see cref="RoomJoinRequestViewModel"/>.
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

    private static AngleSharp.Dom.IElement RoomInput(Shown shown) => shown.Dialog.Find("input");

    private static AngleSharp.Dom.IElement ServersInput(Shown shown) => shown.Dialog.Find(".join-servers input");

    private static async Task<RoomJoinRequestViewModel> SubmitAsync(Shown shown)
    {
        shown.Dialog.WaitForAssertion(() => Assert.False(SubmitButton(shown).HasAttribute("disabled")));
        SubmitButton(shown).Click();
        var result = await shown.Reference.Result.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result!.Canceled);
        return Assert.IsType<RoomJoinRequestViewModel>(result.Data);
    }

    [Fact]
    public async Task NamesTheUser_AndTheReferencesConditions()
    {
        var shown = await ShowAsync(Memberships);

        var text = Text(shown.Provider);
        Assert.Contains(L["JoinToRoomTitle", User].Value, text);
        Assert.Contains(L["JoinToRoomDescription", User].Value, text);
        Assert.Contains(L["RoomIdOrAlias"].Value, text);
        Assert.Contains(L["JoinToRoomRoomIdHint"].Value, text);
        Assert.Contains(L["JoinServers"].Value, text);
        Assert.Contains(L["JoinServersHint"].Value, text);
        Assert.False(ServersInput(shown).HasAttribute("disabled"));
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
        var request = await SubmitAsync(shown);
        Assert.Equal("!left:hs", request.Target);
        Assert.Empty(request.Servers);
    }

    /// <summary>Both room ID forms are accepted, the one of room version 12 without a domain included.</summary>
    [Theory]
    [InlineData("  !other:hs ", "!other:hs")]
    [InlineData("!QxR7a-Kq_9abc", "!QxR7a-Kq_9abc")]
    public async Task TypedRoomId_IsReturnedTrimmed(string typed, string expected)
    {
        var shown = await ShowAsync(Memberships);

        RoomInput(shown).Input(typed);

        Assert.Equal(expected, (await SubmitAsync(shown)).Target);
    }

    /// <summary>For a room ID, servers separated by commas or whitespace are returned in their order (#606).</summary>
    [Fact]
    public async Task RoomId_WithServers_ReturnsThem()
    {
        var shown = await ShowAsync(Memberships);

        RoomInput(shown).Input("!left:hs");
        ServersInput(shown).Input("matrix.org, other.org:8448\t[2001:db8::1] ,");

        var request = await SubmitAsync(shown);
        Assert.Equal("!left:hs", request.Target);
        Assert.Equal(["matrix.org", "other.org:8448", "[2001:db8::1]"], request.Servers);
    }

    /// <summary>
    /// An alias, whose localpart may contain a space, is returned trimmed. The servers field is disabled with the note,
    /// and servers typed before aren't returned: the homeserver joins through the alias's own servers (#606).
    /// </summary>
    [Fact]
    public async Task Alias_DisablesTheServers_AndIsReturnedWithoutThem()
    {
        var shown = await ShowAsync(Memberships);
        ServersInput(shown).Input("matrix.org");

        RoomInput(shown).Input("  #my room:hs ");

        shown.Dialog.WaitForAssertion(() => Assert.True(ServersInput(shown).HasAttribute("disabled")));
        Assert.Contains(L["JoinServersAliasNote"].Value, Text(shown.Provider));
        Assert.DoesNotContain(L["JoinServersHint"].Value, Text(shown.Provider));
        var request = await SubmitAsync(shown);
        Assert.Equal("#my room:hs", request.Target);
        Assert.Empty(request.Servers);
    }

    /// <summary>An invalid server doesn't block an alias, for which no servers are sent; back on a room ID, it does.</summary>
    [Fact]
    public async Task InvalidServer_IsIgnoredForAnAlias_AndRefusedForARoomId()
    {
        var shown = await ShowAsync(Memberships);
        RoomInput(shown).Input("!other:hs");
        ServersInput(shown).Input("exa_mple.org");
        shown.Dialog.WaitForAssertion(() => Assert.Contains(L["InvalidServerName", "exa_mple.org"].Value, Text(shown.Provider)));

        RoomInput(shown).Input("#room:hs");

        shown.Dialog.WaitForAssertion(() => Assert.False(SubmitButton(shown).HasAttribute("disabled")));
        Assert.DoesNotContain(L["InvalidServerName", "exa_mple.org"].Value, Text(shown.Provider));

        RoomInput(shown).Input("!other:hs");

        shown.Dialog.WaitForAssertion(() => Assert.Contains(L["InvalidServerName", "exa_mple.org"].Value, Text(shown.Provider)));
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("matrix.org https://other.org", "https://other.org")]
    [InlineData("matrix.org:", "matrix.org:")]
    public async Task InvalidServer_IsNamed_AndRefused(string servers, string named)
    {
        var shown = await ShowAsync(Memberships);

        RoomInput(shown).Input("!other:hs");
        ServersInput(shown).Input(servers);

        shown.Dialog.WaitForAssertion(() => Assert.Contains(L["InvalidServerName", named].Value, Text(shown.Provider)));
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("#room")]
    [InlineData("#:hs")]
    [InlineData("#room:exa mple")]
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
