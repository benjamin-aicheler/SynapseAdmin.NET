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
/// The Redact events dialog (#600, AC-25): the user's rooms or rooms selected from every membership, an optional
/// reason, a search limit of at least 1 and "Redact as admin", with the notes the reference gives. It returns the
/// options and sends nothing.
/// </summary>
public class RedactUserEventsDialogTests : DetailPageTestBase
{
    private const string Target = "Spammer (@spammer:hs)";

    private static readonly List<UserMembershipViewModel> Memberships =
    [
        new() { RoomId = "!joined:hs", Membership = "join" },
        new() { RoomId = "!left:hs", Membership = "leave" },
        new() { RoomId = "!banned:hs", Membership = "ban" },
        new() { RoomId = "!invited:hs", Membership = "invite" },
    ];

    public RedactUserEventsDialogTests()
    {
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IRenderedComponent<MudDialogProvider>? _provider;

    private async Task<(IRenderedComponent<RedactUserEventsDialog> Dialog, IDialogReference Reference)> ShowAsync(List<UserMembershipViewModel>? memberships = null)
    {
        var provider = _provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        var parameters = new DialogParameters
        {
            [nameof(RedactUserEventsDialog.Target)] = Target,
            [nameof(RedactUserEventsDialog.Memberships)] = memberships ?? Memberships,
        };
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>()
            .ShowAsync<RedactUserEventsDialog>("Dialog", parameters));
        var dialog = provider.FindComponent<RedactUserEventsDialog>();
        dialog.WaitForElement(".redact-limit input");
        return (dialog, reference);
    }

    private static AngleSharp.Dom.IElement ContinueButton(IRenderedComponent<RedactUserEventsDialog> dialog) =>
        dialog.FindAll(".mud-dialog-actions button")[^1];

    private static string Text(IRenderedComponent<RedactUserEventsDialog> dialog) => WebUtility.HtmlDecode(dialog.Markup);

    private static void ChooseSelectedRooms(IRenderedComponent<RedactUserEventsDialog> dialog)
    {
        dialog.Find(".redact-selected-rooms input").Click();
        dialog.WaitForElement(".redact-room input");
    }

    private static void SelectRoom(IRenderedComponent<RedactUserEventsDialog> dialog, string roomId) =>
        dialog.FindAll(".redact-room").Single(r => r.TextContent.Contains(roomId)).QuerySelector("input")!.Change(true);

    private static async Task<UserRedactionRequestViewModel> SubmitAsync(IRenderedComponent<RedactUserEventsDialog> dialog, IDialogReference reference)
    {
        dialog.WaitForAssertion(() => Assert.False(ContinueButton(dialog).HasAttribute("disabled")));
        ContinueButton(dialog).Click();
        var result = await reference.Result.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result!.Canceled);
        return Assert.IsType<UserRedactionRequestViewModel>(result.Data);
    }

    /// <summary>"The user's rooms" counts only the joined and banned rooms: those Synapse redacts in for an empty list.</summary>
    [Fact]
    public async Task ShowsTheUser_TheUserRoomCount_AndTheNotes()
    {
        var (dialog, _) = await ShowAsync();

        // MudDialog renders the title outside the dialog component's own markup.
        Assert.Contains(L["RedactUserEventsTitle", Target].Value, WebUtility.HtmlDecode(_provider!.Markup));
        var text = Text(dialog);
        Assert.Contains(L["RedactUserRooms", 2].Value, text);
        Assert.Contains(L["RedactEventTypesHint"].Value, text);
        Assert.Contains(L["RedactReasonHint"].Value, text);
        Assert.Contains(L["RedactSearchLimitHint"].Value, text);
        Assert.Contains(L["RedactAsAdminHint"].Value, text);
        Assert.DoesNotContain(L["RedactUserRoomsNone"].Value, text);
        Assert.Empty(dialog.FindAll(".redact-room"));
    }

    [Fact]
    public async Task UserInNoRoom_SaysTheUserRoomsRedactNothing()
    {
        var (dialog, _) = await ShowAsync([new() { RoomId = "!left:hs", Membership = "leave" }]);

        var text = Text(dialog);
        Assert.Contains(L["RedactUserRooms", 0].Value, text);
        Assert.Contains(L["RedactUserRoomsNone"].Value, text);
    }

    [Fact]
    public async Task Defaults_AreTheUserRooms_WithSynapsesLimit()
    {
        var (dialog, reference) = await ShowAsync();

        var request = await SubmitAsync(dialog, reference);

        Assert.True(request.AllRooms);
        Assert.Empty(request.RoomIds);
        Assert.Null(request.Reason);
        Assert.Equal((UserRedactionRequestViewModel.DefaultLimit, false), (request.Limit, request.UseAdmin));
    }

    /// <summary>Every membership can be selected, rooms the user left included, each with its membership.</summary>
    [Fact]
    public async Task SelectedRooms_ListsEveryMembership()
    {
        var (dialog, _) = await ShowAsync();

        ChooseSelectedRooms(dialog);

        var rows = dialog.FindAll(".redact-room");
        Assert.Equal(Memberships.Select(m => m.RoomId), rows.Select(r => Memberships.Single(m => r.TextContent.Contains(m.RoomId)).RoomId));
        Assert.All(rows.Zip(Memberships), p => Assert.Equal(p.Second.Membership, p.First.QuerySelector(".mud-chip")!.TextContent.Trim()));
    }

    /// <summary>An empty selection would mean the user's rooms, so it can't be submitted.</summary>
    [Fact]
    public async Task SelectedRooms_WithoutARoom_CannotBeSubmitted()
    {
        var (dialog, reference) = await ShowAsync();

        ChooseSelectedRooms(dialog);
        ContinueButton(dialog).Click();

        Assert.True(ContinueButton(dialog).HasAttribute("disabled"));
        Assert.False(reference.Result.IsCompleted);
    }

    [Fact]
    public async Task SelectedRooms_ReturnsThem_InTheMembershipOrder()
    {
        var (dialog, reference) = await ShowAsync();

        ChooseSelectedRooms(dialog);
        SelectRoom(dialog, "!banned:hs");
        SelectRoom(dialog, "!left:hs");
        var request = await SubmitAsync(dialog, reference);

        Assert.False(request.AllRooms);
        Assert.Equal(["!left:hs", "!banned:hs"], request.RoomIds);
    }

    [Fact]
    public async Task SelectedRooms_ARoomUnselectedAgain_IsNotReturned()
    {
        var (dialog, reference) = await ShowAsync();

        ChooseSelectedRooms(dialog);
        SelectRoom(dialog, "!joined:hs");
        SelectRoom(dialog, "!left:hs");
        dialog.FindAll(".redact-room").Single(r => r.TextContent.Contains("!joined:hs")).QuerySelector("input")!.Change(false);
        var request = await SubmitAsync(dialog, reference);

        Assert.Equal(["!left:hs"], request.RoomIds);
    }

    /// <summary>Rooms selected before switching back to the user's rooms aren't sent.</summary>
    [Fact]
    public async Task BackToTheUserRooms_ReturnsNoRoomIds()
    {
        var (dialog, reference) = await ShowAsync();

        ChooseSelectedRooms(dialog);
        SelectRoom(dialog, "!left:hs");
        dialog.Find(".redact-user-rooms input").Click();
        var request = await SubmitAsync(dialog, reference);

        Assert.True(request.AllRooms);
        Assert.Empty(request.RoomIds);
    }

    [Fact]
    public async Task ReturnsTheReason_TheLimit_AndRedactAsAdmin()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find(".redact-reason input").Input("Spam");
        dialog.Find(".redact-limit input").Input("50");
        dialog.Find(".redact-as-admin input").Change(true);
        var request = await SubmitAsync(dialog, reference);

        Assert.Equal(("Spam", 50, true), (request.Reason, request.Limit, request.UseAdmin));
    }

    [Fact]
    public async Task BlankReason_IsReturnedAsNone()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find(".redact-reason input").Input("   ");
        var request = await SubmitAsync(dialog, reference);

        Assert.Null(request.Reason);
    }

    /// <summary>Synapse would take 0 as its default of 1000, so the field raises a lower limit to 1.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task LimitBelowOne_BecomesOne(string limit)
    {
        var (dialog, reference) = await ShowAsync();

        dialog.Find(".redact-limit input").Input(limit);

        Assert.Equal("1", dialog.Find(".redact-limit input").GetAttribute("value"));
        Assert.Equal(1, (await SubmitAsync(dialog, reference)).Limit);
    }

    [Fact]
    public async Task Cancel_ReturnsNothing()
    {
        var (dialog, reference) = await ShowAsync();

        dialog.FindAll(".mud-dialog-actions button")[0].Click();

        Assert.True((await reference.Result.WaitAsync(TimeSpan.FromSeconds(10)))!.Canceled);
    }
}
