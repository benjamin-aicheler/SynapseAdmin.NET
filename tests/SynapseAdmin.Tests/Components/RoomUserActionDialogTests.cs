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
/// The room recovery dialog (#587, AC-26): it names the room and describes the action as the reference does. Make room
/// admin takes an empty ID (the signed-in admin) or a full user ID on any server and returns the trimmed ID; Join takes a
/// full user ID on the admin's server, compared exactly, that isn't a joined member, and servers to join through (#606,
/// AC-28), and returns both as a <see cref="RoomJoinRequestViewModel"/>.
/// </summary>
public class RoomUserActionDialogTests : DetailPageTestBase
{
    private const string Room = "Lobby (!room:example.com)";
    private const string ServerName = "example.com";
    private const string Admin = "@admin:example.com";

    public RoomUserActionDialogTests()
    {
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private sealed record Shown(
        IRenderedComponent<MudDialogProvider> Provider,
        IRenderedComponent<RoomUserActionDialog> Dialog,
        IDialogReference Reference);

    private async Task<Shown> ShowAsync(RoomUserAction action, params string[] joinedMembers)
    {
        var provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        var parameters = new DialogParameters
        {
            [nameof(RoomUserActionDialog.Action)] = action,
            [nameof(RoomUserActionDialog.RoomLabel)] = Room,
            [nameof(RoomUserActionDialog.SignedInUserId)] = Admin,
            [nameof(RoomUserActionDialog.ServerName)] = ServerName,
            [nameof(RoomUserActionDialog.JoinedMemberIds)] = (IReadOnlyCollection<string>)joinedMembers,
        };
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>()
            .ShowAsync<RoomUserActionDialog>("Dialog", parameters));
        var dialog = provider.FindComponent<RoomUserActionDialog>();
        dialog.WaitForElement("input");
        return new Shown(provider, dialog, reference);
    }

    private static AngleSharp.Dom.IElement SubmitButton(Shown shown) =>
        shown.Dialog.FindAll(".mud-dialog-actions button")[^1];

    private static string Text(IRenderedComponent<MudDialogProvider> provider) => WebUtility.HtmlDecode(provider.Markup);

    private static AngleSharp.Dom.IElement ServersInput(Shown shown) => shown.Dialog.Find(".join-servers input");

    private static async Task<object?> SubmitAsync(Shown shown)
    {
        shown.Dialog.WaitForAssertion(() => Assert.False(SubmitButton(shown).HasAttribute("disabled")));
        SubmitButton(shown).Click();
        var result = await shown.Reference.Result.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result!.Canceled);
        return result.Data;
    }

    private static async Task<string> SubmitUserIdAsync(Shown shown) => Assert.IsType<string>(await SubmitAsync(shown));

    private static async Task<RoomJoinRequestViewModel> SubmitJoinAsync(Shown shown) =>
        Assert.IsType<RoomJoinRequestViewModel>(await SubmitAsync(shown));

    [Fact]
    public async Task MakeAdmin_NamesTheRoom_TheEffectAndThePrecondition()
    {
        var shown = await ShowAsync(RoomUserAction.MakeAdmin);

        var text = Text(shown.Provider);
        Assert.Contains(L["MakeRoomAdminTitle"].Value, text);
        Assert.Contains(L["MakeRoomAdminDescription", Room].Value, text);
        Assert.Contains(L["MakeRoomAdminPrecondition"].Value, text);
        Assert.Contains(L["MakeRoomAdminUserHint", Admin].Value, text);
        Assert.Empty(shown.Dialog.FindAll(".join-servers"));
        Assert.DoesNotContain(L["JoinServers"].Value, text);
        Assert.Equal(L["MakeRoomAdmin"].Value, SubmitButton(shown).TextContent.Trim());
    }

    [Fact]
    public async Task Join_NamesTheRoom_AndTheReferencesConditions()
    {
        var shown = await ShowAsync(RoomUserAction.Join);

        var text = Text(shown.Provider);
        Assert.Contains(L["JoinUserTitle"].Value, text);
        Assert.Contains(L["JoinUserDescription", Room].Value, text);
        Assert.Contains(L["JoinUserHint", ServerName].Value, text);
        Assert.DoesNotContain(L["MakeRoomAdminPrecondition"].Value, text);
        Assert.Contains(L["JoinServers"].Value, text);
        Assert.Contains(L["JoinServersHint"].Value, text);
        Assert.False(ServersInput(shown).HasAttribute("disabled"));
        Assert.Equal(L["JoinUser"].Value, SubmitButton(shown).TextContent.Trim());
    }

    /// <summary>Empty is the signed-in admin: the dialog returns <c>""</c>, and the service sends no user.</summary>
    [Fact]
    public async Task MakeAdmin_Empty_ReturnsAnEmptyId()
    {
        var shown = await ShowAsync(RoomUserAction.MakeAdmin);

        Assert.Equal("", await SubmitUserIdAsync(shown));
    }

    /// <summary>Synapse doesn't check the server of the user it makes room admin.</summary>
    [Fact]
    public async Task MakeAdmin_UserOnAnotherServer_ReturnsTheTrimmedId()
    {
        var shown = await ShowAsync(RoomUserAction.MakeAdmin);

        shown.Dialog.Find("input").Input("  @bob:other.org ");

        Assert.Equal("@bob:other.org", await SubmitUserIdAsync(shown));
    }

    [Theory]
    [InlineData(RoomUserAction.MakeAdmin, "bob")]
    [InlineData(RoomUserAction.MakeAdmin, "@bob")]
    [InlineData(RoomUserAction.Join, "@bob:")]
    [InlineData(RoomUserAction.Join, "#room:example.com")]
    public async Task MalformedId_IsRefused(RoomUserAction action, string userId)
    {
        var shown = await ShowAsync(action);

        shown.Dialog.Find("input").Input(userId);

        shown.Dialog.WaitForAssertion(() => Assert.Contains(L["InvalidUserId"].Value, Text(shown.Provider)));
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Join_Empty_CantBeSubmitted_WithoutAnError()
    {
        var shown = await ShowAsync(RoomUserAction.Join);

        shown.Dialog.Find("input").Input("   ");

        shown.Dialog.WaitForAssertion(() => Assert.True(SubmitButton(shown).HasAttribute("disabled")));
        Assert.DoesNotContain(L["InvalidUserId"].Value, Text(shown.Provider));
    }

    /// <summary>Exact, as Synapse and Tuwunel compare the server name: a mixed-case server isn't this one.</summary>
    [Theory]
    [InlineData("@bob:other.org")]
    [InlineData("@bob:Example.com")]
    [InlineData("@bob:example.com.evil")]
    public async Task Join_UserNotOnThisServer_IsRefused(string userId)
    {
        var shown = await ShowAsync(RoomUserAction.Join);

        shown.Dialog.Find("input").Input(userId);

        shown.Dialog.WaitForAssertion(() => Assert.Contains(L["JoinUserNotLocal", ServerName].Value, Text(shown.Provider)));
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    /// <summary>Synapse invites before it joins, and inviting a joined member fails.</summary>
    [Fact]
    public async Task Join_JoinedMember_IsRefused()
    {
        var shown = await ShowAsync(RoomUserAction.Join, "@bob:example.com", "@carol:example.com");

        shown.Dialog.Find("input").Input(" @bob:example.com");

        shown.Dialog.WaitForAssertion(() => Assert.Contains(L["UserAlreadyInRoom"].Value, Text(shown.Provider)));
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Join_LocalUser_ReturnsTheTrimmedId_WithoutServers()
    {
        var shown = await ShowAsync(RoomUserAction.Join, "@carol:example.com");

        shown.Dialog.Find("input").Input(" @bob:example.com ");

        var request = await SubmitJoinAsync(shown);
        Assert.Equal("@bob:example.com", request.Target);
        Assert.Empty(request.Servers);
    }

    /// <summary>Servers are separated by commas or whitespace, in their order; the service drops duplicates (#606).</summary>
    [Fact]
    public async Task Join_Servers_AreSplit_AndReturned()
    {
        var shown = await ShowAsync(RoomUserAction.Join);

        shown.Dialog.Find("input").Input("@bob:example.com");
        ServersInput(shown).Input(" matrix.org,other.org:8448  [::1]:8448 ,, matrix.org ");

        var request = await SubmitJoinAsync(shown);
        Assert.Equal(["matrix.org", "other.org:8448", "[::1]:8448", "matrix.org"], request.Servers);
    }

    [Theory]
    [InlineData("matrix.org, https://other.org", "https://other.org")]
    [InlineData("exa_mple.org", "exa_mple.org")]
    [InlineData("matrix.org:", "matrix.org:")]
    public async Task Join_InvalidServer_IsNamed_AndRefused(string servers, string named)
    {
        var shown = await ShowAsync(RoomUserAction.Join);

        shown.Dialog.Find("input").Input("@bob:example.com");
        ServersInput(shown).Input(servers);

        shown.Dialog.WaitForAssertion(() => Assert.Contains(L["InvalidServerName", named].Value, Text(shown.Provider)));
        Assert.True(SubmitButton(shown).HasAttribute("disabled"));
    }

    /// <summary>Correcting the servers enables the button again.</summary>
    [Fact]
    public async Task Join_CorrectedServer_CanBeSubmitted()
    {
        var shown = await ShowAsync(RoomUserAction.Join);
        shown.Dialog.Find("input").Input("@bob:example.com");
        ServersInput(shown).Input("exa_mple.org");
        shown.Dialog.WaitForAssertion(() => Assert.True(SubmitButton(shown).HasAttribute("disabled")));

        ServersInput(shown).Input("example.org");

        Assert.Equal(["example.org"], (await SubmitJoinAsync(shown)).Servers);
        Assert.DoesNotContain(L["InvalidServerName", "exa_mple.org"].Value, Text(shown.Provider));
    }

    [Fact]
    public async Task Cancel_ClosesAsCancelled()
    {
        var shown = await ShowAsync(RoomUserAction.Join);

        shown.Dialog.FindAll(".mud-dialog-actions button")[0].Click();

        var result = await shown.Reference.Result.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result!.Canceled);
    }
}
