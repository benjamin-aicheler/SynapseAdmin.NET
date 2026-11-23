using Bunit;
using Microsoft.AspNetCore.Components;
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
/// What the room delete handlers show and do, and which media tables the single-media actions reload.
/// </summary>
public class RoomDetailsActionTests : DetailPageTestBase
{
    private const string RoomId = "!a:hs";
    private const string Local = "mxc://hs/local";
    private const string Remote = "mxc://remote/remote";

    private readonly Stub _rooms;
    private OperationResult _result = OperationResult.Ok();

    public RoomDetailsActionTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), _ => Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(
                new RoomDetailViewModel
                {
                    RoomId = RoomId,
                    Name = "Alpha Lounge",
                    Media = new RoomMediaViewModel { Local = [new() { MediaId = Local }], Remote = [new() { MediaId = Remote }] }
                })))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ =>
                Task.FromResult(OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel())))
            .On(nameof(IRoomService.GetMediaMetadataBatchAsync), args => Task.FromResult(OperationResult<List<RoomMediaItemViewModel>>.Ok(
                ((List<string>)args[0]!).Select(m => new RoomMediaItemViewModel { MediaId = m }).ToList())))
            .On(nameof(IRoomService.DeleteRoomAsync), _ => Task.FromResult(WithData<string>(_result)));

        // Replaces the base class's unconfigured media service: every action returns the current _result.
        Services.AddSingleton(Stub.Create<IMediaService>(s =>
        {
            foreach (var method in typeof(IMediaService).GetMethods().Where(m => m.ReturnType == typeof(Task<OperationResult>)))
            {
                s.On(method.Name, _ => Task.FromResult(_result));
            }
        }));
    }

    private IStringLocalizer<SharedResources> Localizer => Services.GetRequiredService<IStringLocalizer<SharedResources>>();
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private int BatchCalls(string mxc) =>
        _rooms.CallsTo(nameof(IRoomService.GetMediaMetadataBatchAsync)).Count(a => ((List<string>)a[0]!).Contains(mxc));

    private int RoomCalls => _rooms.CallsTo(nameof(IRoomService.GetRoomDetailsAsync)).Count;

    private IRenderedComponent<RoomDetails> RenderRoom()
    {
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, RoomId));
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));
        return cut;
    }

    private async Task ConfirmAsync(IRenderedComponent<RoomDetails> cut, string handler, params object?[] args)
    {
        var action = InvokeHandler(cut, handler, args);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;
    }

    [Theory]
    [InlineData("DeleteRoom", "DeleteRoomTitle", "DeleteRoomConfirmation", "Delete", true)]
    [InlineData("DeleteRoom", "DeleteRoomTitle", "DeleteRoomConfirmation", "Delete", false)]
    [InlineData("DeleteAndBlockRoom", "DeleteAndBlockRoomTitle", "DeleteAndBlockRoomConfirmation", "DeleteAndBlock", true)]
    [InlineData("DeleteAndBlockRoom", "DeleteAndBlockRoomTitle", "DeleteAndBlockRoomConfirmation", "DeleteAndBlock", false)]
    public async Task DeleteHandler_ShowsItsTexts_AndReturnsToTheRoomListOnlyOnSuccess(
        string handler, string titleKey, string confirmKey, string yesKey, bool success)
    {
        _result = success ? OperationResult.Ok() : OperationResult.Failure("Delete failed");
        var cut = RenderRoom();
        var roomUri = Navigation.Uri;

        await ConfirmAsync(cut, handler);

        var dialog = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowMessageBoxAsync)));
        Assert.Equal(Localizer[titleKey].Value, dialog[0]);
        Assert.Equal(Localizer[confirmKey, $"Alpha Lounge ({RoomId})"].Value, dialog[1]);
        Assert.Equal(Localizer[yesKey].Value, dialog[2]);
        Assert.Equal(Localizer["Cancel"].Value, dialog[4]);

        Assert.Equal(success ? Navigation.ToAbsoluteUri("/rooms").ToString() : roomUri, Navigation.Uri);
        var snackbar = Assert.Single(SnackbarStub.CallsTo(nameof(ISnackbar.Add)));
        Assert.Equal(_result.Message, snackbar[0]);
        Assert.Equal(_result.Severity, snackbar[1]);
    }

    /// <summary>
    /// After a successful single-media action the media tables reload: both for quarantine, unquarantine and delete
    /// (delete also reloads the room), only the local table for a protection toggle. A failure reloads nothing.
    /// </summary>
    [Theory]
    [InlineData("QuarantineSingleMedia", true, 1, 1, 0)]
    [InlineData("UnquarantineSingleMedia", true, 1, 1, 0)]
    [InlineData("DeleteSingleMedia", true, 1, 1, 1)]
    [InlineData("ToggleMediaProtection", true, 1, 0, 0)]
    [InlineData("QuarantineSingleMedia", false, 0, 0, 0)]
    [InlineData("UnquarantineSingleMedia", false, 0, 0, 0)]
    [InlineData("DeleteSingleMedia", false, 0, 0, 0)]
    [InlineData("ToggleMediaProtection", false, 0, 0, 0)]
    public async Task SingleMediaAction_ReloadsTheMediaTablesOnlyOnSuccess(
        string handler, bool success, int localReloads, int remoteReloads, int roomReloads)
    {
        _result = success ? OperationResult.Ok() : OperationResult.Failure("Media action failed");
        var cut = RenderRoom();
        cut.FindAll(".mud-tab").First(t => t.TextContent.Trim() == Localizer["Media"].Value).Click();
        cut.WaitForAssertion(() => Assert.Equal((1, 1), (BatchCalls(Local), BatchCalls(Remote))));
        var roomCalls = RoomCalls;

        if (handler == "ToggleMediaProtection")
        {
            await InvokeHandler(cut, handler, new RoomMediaItemViewModel { MediaId = Local });
        }
        else
        {
            await ConfirmAsync(cut, handler, Local);
        }

        Assert.Equal((1 + localReloads, 1 + remoteReloads, roomCalls + roomReloads), (BatchCalls(Local), BatchCalls(Remote), RoomCalls));
    }
}
