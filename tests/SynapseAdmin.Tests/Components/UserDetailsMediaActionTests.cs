using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The single-upload actions on the user media tab: the MXC URI they send, how they update the shown page, and that
/// they do nothing without a session.
/// </summary>
public class UserDetailsMediaActionTests : DetailPageTestBase
{
    private readonly Stub _users;
    private readonly Stub _media;
    private IMatrixGateway? _gateway = Stub.Create<IMatrixGateway>(s => s
        .On("get_SupportsAdminApi", _ => true)
        .On("get_ServerName", _ => "hs"));
    private OperationResult _result = OperationResult.Ok();

    public UserDetailsMediaActionTests()
    {
        // Replaces the base class's session with one whose gateway a test can remove.
        Services.AddSingleton(Stub.Create<IMatrixSessionService>(s => s
            .On("get_Gateway", _ => _gateway)
            .On("get_IsLoggedIn", _ => _gateway != null)));
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), args => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                new UserDetailViewModel { UserId = (string)args[0]!, DisplayName = "Alice Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(OperationResult<UserMediaPageViewModel>.Ok(
                new UserMediaPageViewModel
                {
                    TotalCount = 2,
                    Items =
                    [
                        new() { MediaId = "upload1", MediaType = "image/png" },
                        new() { MediaId = "upload2", QuarantinedBy = "@mod:hs", SafeFromQuarantine = true }
                    ]
                })))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel { Count = 2 })));

        // Replaces the base class's unconfigured media service: every action returns the current _result.
        Services.AddSingleton(Stub.Create<IMediaService>(out _media));
        foreach (var method in typeof(IMediaService).GetMethods().Where(m => m.ReturnType == typeof(Task<OperationResult>)))
        {
            _media.On(method.Name, _ => Task.FromResult(_result));
        }
    }

    private int PageRequests => _users.CallsTo(nameof(IUserService.GetUserMediaPageAsync)).Count;

    private IRenderedComponent<UserDetails> RenderMediaTab()
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, "@alice:hs"));
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));
        var label = Services.GetRequiredService<IStringLocalizer<SharedResources>>()["Media"].Value;
        cut.FindAll(".mud-tab").First(t => t.TextContent.Trim() == label).Click();
        cut.WaitForAssertion(() => Assert.NotNull(GetField<UserMediaPageViewModel>(cut.Instance, "_mediaPage")));
        return cut;
    }

    private static UserMediaItemViewModel Item(IRenderedComponent<UserDetails> cut, string mediaId) =>
        GetField<UserMediaPageViewModel>(cut.Instance, "_mediaPage")!.Items.Single(m => m.MediaId == mediaId);

    private async Task ConfirmAsync(IRenderedComponent<UserDetails> cut, string handler, string mediaId)
    {
        var action = InvokeHandler(cut, handler, mediaId);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;
    }

    /// <summary>
    /// On success the shown item is patched in place, without reloading the page: quarantine sets the optimistic
    /// QuarantinedBy "Admin", unquarantine clears it. A failure leaves the item as it was.
    /// </summary>
    [Theory]
    [InlineData("QuarantineSingleMedia", "upload1", nameof(IMediaService.QuarantineMediaAsync), true, "Admin")]
    [InlineData("QuarantineSingleMedia", "upload1", nameof(IMediaService.QuarantineMediaAsync), false, null)]
    [InlineData("UnquarantineSingleMedia", "upload2", nameof(IMediaService.UnquarantineMediaAsync), true, null)]
    [InlineData("UnquarantineSingleMedia", "upload2", nameof(IMediaService.UnquarantineMediaAsync), false, "@mod:hs")]
    public async Task QuarantineAction_SendsTheMxcUri_AndPatchesTheShownItemOnSuccess(
        string handler, string mediaId, string serviceMethod, bool success, string? quarantinedBy)
    {
        _result = success ? OperationResult.Ok() : OperationResult.Failure("failed");
        var cut = RenderMediaTab();
        var pageRequests = PageRequests;

        await ConfirmAsync(cut, handler, mediaId);

        Assert.Equal($"mxc://hs/{mediaId}", Assert.Single(_media.CallsTo(serviceMethod))[0]);
        Assert.Equal(quarantinedBy, Item(cut, mediaId).QuarantinedBy);
        Assert.Equal(pageRequests, PageRequests);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task DeleteAction_SendsTheMxcUri_AndReloadsThePageOnSuccess(bool success, int reloads)
    {
        _result = success ? OperationResult.Ok() : OperationResult.Failure("failed");
        var cut = RenderMediaTab();
        var pageRequests = PageRequests;

        await ConfirmAsync(cut, "DeleteSingleMedia", "upload1");

        Assert.Equal("mxc://hs/upload1", Assert.Single(_media.CallsTo(nameof(IMediaService.DeleteMediaAsync)))[0]);
        Assert.Equal(pageRequests + reloads, PageRequests);
    }

    /// <summary>Protection is toggled without a confirmation; on success the item's flag flips.</summary>
    [Theory]
    [InlineData("upload1", nameof(IMediaService.ProtectMediaAsync), true, true)]
    [InlineData("upload1", nameof(IMediaService.ProtectMediaAsync), false, false)]
    [InlineData("upload2", nameof(IMediaService.UnprotectMediaAsync), true, false)]
    public async Task ToggleProtection_SendsTheMxcUri_AndFlipsTheFlagOnSuccess(
        string mediaId, string serviceMethod, bool success, bool protectedAfter)
    {
        _result = success ? OperationResult.Ok() : OperationResult.Failure("failed");
        var cut = RenderMediaTab();

        await InvokeHandler(cut, "ToggleMediaProtection", Item(cut, mediaId));

        Assert.Empty(Confirmations);
        Assert.Equal($"mxc://hs/{mediaId}", Assert.Single(_media.CallsTo(serviceMethod))[0]);
        Assert.Equal(protectedAfter, Item(cut, mediaId).SafeFromQuarantine);
    }

    [Fact]
    public async Task ShowPreview_OpensThePreviewOfTheMxcUri()
    {
        var cut = RenderMediaTab();

        await InvokeHandler(cut, "ShowPreview", Item(cut, "upload1"));

        var parameters = (DialogParameters)Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)))[1]!;
        Assert.Equal("/Media/Preview?mxc=mxc%3A%2F%2Fhs%2Fupload1&mimeType=image%2Fpng", parameters["PreviewUrl"]);
    }

    /// <summary>The URL uses the resolved MXC URI, the Title parameter falls back to the bare media ID.</summary>
    [Theory]
    [InlineData("cat & dog.svg", "image/svg+xml", "/Media/Preview?mxc=mxc%3A%2F%2Fhs%2Fa%2Bb&mimeType=image%2Fsvg%2Bxml")]
    [InlineData(null, null, "/Media/Preview?mxc=mxc%3A%2F%2Fhs%2Fa%2Bb&mimeType=")]
    public async Task ShowPreview_OpensTheDialog(string? uploadName, string? mediaType, string previewUrl)
    {
        var cut = RenderMediaTab();

        await InvokeHandler(cut, "ShowPreview", new UserMediaItemViewModel { MediaId = "a+b", UploadName = uploadName, MediaType = mediaType });

        var fallbackTitle = Services.GetRequiredService<IStringLocalizer<SharedResources>>()["MediaPreview"].Value;
        AssertPreviewShown(uploadName ?? fallbackTitle, uploadName ?? "a+b", previewUrl, mediaType);
    }

    /// <summary>Without a session every action returns before its confirmation, dialog or service call.</summary>
    [Theory]
    [InlineData("QuarantineSingleMedia")]
    [InlineData("UnquarantineSingleMedia")]
    [InlineData("DeleteSingleMedia")]
    [InlineData("ToggleMediaProtection")]
    [InlineData("ShowPreview")]
    public async Task WithoutSession_MediaActionDoesNothing(string handler)
    {
        var cut = RenderMediaTab();
        object argument = handler is "ToggleMediaProtection" or "ShowPreview" ? Item(cut, "upload1") : "upload1";
        _gateway = null;

        // A shown confirmation is never answered here, so time out instead of hanging.
        await InvokeHandler(cut, handler, argument).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(DialogStub.Calls);
        Assert.Empty(_media.Calls);
    }
}
