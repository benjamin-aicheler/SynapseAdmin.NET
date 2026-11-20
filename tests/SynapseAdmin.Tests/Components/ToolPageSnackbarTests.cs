using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which results the room directory, media maintenance and server notice pages report in a snackbar: every result
/// except a silent cancellation, also a directory load or a maintenance run that succeeded without data.
/// </summary>
public class ToolPageSnackbarTests : DetailPageTestBase
{
    private readonly Stub _rooms;
    private OperationResult _result = new();
    private OperationResult<PublicRoomDirectoryViewModel> _directory =
        OperationResult<PublicRoomDirectoryViewModel>.Ok(new PublicRoomDirectoryViewModel());
    private OperationResult<SynapseAdminPurgeMediaCacheResponse> _purge = new();
    private OperationResult<SynapseAdminDeleteMediaResponse> _delete = new();

    public ToolPageSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetPublicRoomsAsync), _ => Task.FromResult(_directory))
            .On(nameof(IRoomService.SetRoomDirectoryVisibilityAsync), _ => Task.FromResult(_result));
        Services.AddSingleton(Stub.Create<IMediaService>(s => s
            .On(nameof(IMediaService.PurgeRemoteMediaCacheAsync), _ => Task.FromResult(_purge))
            .On(nameof(IMediaService.DeleteLocalMediaAsync), _ => Task.FromResult(_delete))));
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.SendServerNoticeAsync), _ => Task.FromResult(_result))));
    }

    private async Task ConfirmAsync<TPage>(IRenderedComponent<TPage> cut, string handler, params object?[] args) where TPage : IComponent
    {
        var action = InvokeHandler(cut, handler, args);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;
    }

    [Theory]
    [InlineData("success", true)]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    public void DirectoryLoad_ReportsEveryResultWithoutADirectoryExceptACancellation(string kind, bool shown)
    {
        _directory = Make<OperationResult<PublicRoomDirectoryViewModel>>(kind);

        Render<RoomDirectory>();

        AssertSnackbars(shown ? [_directory] : []);
    }

    // Unlike the other dialogs, these two have a close button and leave Escape at the provider default (off).
    [Fact]
    public async Task DirectoryDialogs_HaveACloseButton_InsteadOfClosingOnEscape()
    {
        CancelDialogs();
        var cut = Render<RoomDirectory>();

        await InvokeHandler(cut, "OpenPublishRoomDialog");
        await InvokeHandler(cut, "OpenManageAliasesDialog", "!room:example.com", null);

        AssertDialogOptions(
            new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true },
            new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true });
    }

    /// <summary>After a success the directory reloads, which reports nothing.</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public async Task RemoveFromDirectory_ReportsEveryResultExceptACancellation(string kind)
    {
        _result = Make<OperationResult>(kind);
        var cut = Render<RoomDirectory>();

        await ConfirmAsync(cut, "RemoveFromDirectoryAsync", "!a:hs");

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }

    /// <summary>A run that succeeded without data is reported by the failure branch, with its own severity.</summary>
    [Theory]
    [InlineData("ConfirmPurgeRemoteCache", "success")]
    [InlineData("ConfirmPurgeRemoteCache", "failure")]
    [InlineData("ConfirmPurgeRemoteCache", "cancelled")]
    [InlineData("ConfirmDeleteLocalMedia", "success")]
    [InlineData("ConfirmDeleteLocalMedia", "failure")]
    [InlineData("ConfirmDeleteLocalMedia", "cancelled")]
    public async Task MediaMaintenance_ReportsEveryResultExceptACancellation(string handler, string kind)
    {
        OperationResult result = handler == "ConfirmPurgeRemoteCache"
            ? _purge = Make<OperationResult<SynapseAdminPurgeMediaCacheResponse>>(kind)
            : _delete = Make<OperationResult<SynapseAdminDeleteMediaResponse>>(kind);
        var cut = Render<MediaMaintenance>();

        await ConfirmAsync(cut, handler);

        AssertSnackbars(kind == "cancelled" ? [] : [result]);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public async Task SendNotice_ReportsEveryResultExceptACancellation(string kind)
    {
        _result = Make<OperationResult>(kind);
        var cut = Render<ServerNotices>();

        await InvokeHandler(cut, "SendNotice");

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }
}
