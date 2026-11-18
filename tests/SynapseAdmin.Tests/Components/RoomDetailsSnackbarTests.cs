using System.Net;
using Bunit;
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
/// Which results the room page reports in a snackbar: the actions, the room load and a media table page report every
/// result except a silent cancellation, while the history load reports only failures.
/// </summary>
public class RoomDetailsSnackbarTests : DetailPageTestBase
{
    private const string RoomId = "!a:hs";
    private const string Mxc = "mxc://hs/a";

    private readonly Stub _rooms;
    private OperationResult _result = new();
    private OperationResult<RoomDetailViewModel> _room = OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel
    {
        RoomId = RoomId, Name = "Alpha Lounge", Media = new RoomMediaViewModel { Local = [new() { MediaId = Mxc }] }
    });
    private OperationResult<RoomMessagesViewModel> _messages = OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel());
    private OperationResult<List<RoomMediaItemViewModel>> _media = OperationResult<List<RoomMediaItemViewModel>>.Ok([new() { MediaId = Mxc }]);

    public RoomDetailsSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), _ => Task.FromResult(_room))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ => Task.FromResult(_messages))
            .On(nameof(IRoomService.GetMediaMetadataBatchAsync), _ => Task.FromResult(_media))
            .On(nameof(IRoomService.DeleteRoomAsync), _ => Task.FromResult(_result))
            .On(nameof(IRoomService.QuarantineMediaAsync), _ => Task.FromResult(_result))
            .On(nameof(IRoomService.BlockRoomAsync), _ => Task.FromResult(_result));
        Services.AddSingleton(Stub.Create<IMediaService>(s =>
        {
            foreach (var method in typeof(IMediaService).GetMethods().Where(m => m.ReturnType == typeof(Task<OperationResult>)))
            {
                s.On(method.Name, _ => Task.FromResult(_result));
            }
        }));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    /// <summary>"failure" fails, "cancelled" is a silent cancellation, "success" succeeds without data.</summary>
    private static TResult Make<TResult>(string kind) where TResult : OperationResult, new() => kind switch
    {
        "failure" => new TResult { Success = false, Message = "Failed", Severity = Severity.Error },
        "cancelled" => new TResult { Success = false, Message = "Operation cancelled", Severity = Severity.Normal },
        _ => new TResult { Success = true, Message = "Done", Severity = Severity.Success }
    };

    private void AssertSnackbars(params OperationResult[] shown) =>
        Assert.Equal(
            shown.Select(r => ((string?)r.Message, r.Severity)),
            SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Select(a => ((string?)a[0], (Severity)a[1]!)));

    private IRenderedComponent<RoomDetails> RenderRoom()
    {
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, RoomId));
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));
        return cut;
    }

    public static TheoryData<string, string> Actions()
    {
        var data = new TheoryData<string, string>();
        foreach (var handler in new[] { "DeleteRoom", "QuarantineMedia", "BlockRoom", "QuarantineSingleMedia", "UnquarantineSingleMedia", "DeleteSingleMedia", "ToggleMediaProtection" })
        {
            foreach (var kind in new[] { "success", "failure", "cancelled" }) data.Add(handler, kind);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Action_ReportsEveryResultExceptACancellation(string handler, string kind)
    {
        _result = Make<OperationResult>(kind);
        var cut = RenderRoom();
        object?[] args = handler switch
        {
            "ToggleMediaProtection" => [new RoomMediaItemViewModel { MediaId = Mxc }],
            "QuarantineSingleMedia" or "UnquarantineSingleMedia" or "DeleteSingleMedia" => [Mxc],
            _ => []
        };

        var action = InvokeHandler(cut, handler, args);
        if (handler is not ("BlockRoom" or "ToggleMediaProtection"))
        {
            cut.WaitForAssertion(() => Assert.Single(Confirmations));
            Confirmations[0].Result.SetResult(true);
        }
        await action;

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }

    /// <summary>A reported room-load result also replaces the page's "room not found" text, even a success without a room.</summary>
    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", true)]
    public void RoomLoad_ReportsEveryResultExceptACancellation(string kind, bool shown)
    {
        _room = Make<OperationResult<RoomDetailViewModel>>(kind);

        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, RoomId));

        cut.WaitForAssertion(() => Assert.Contains(shown ? _room.Message : L["RoomNotFound"].Value, WebUtility.HtmlDecode(cut.Markup)));
        AssertSnackbars(shown ? [_room] : []);
    }

    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", false)]
    public void HistoryLoad_ReportsOnlyFailures(string kind, bool shown)
    {
        _messages = Make<OperationResult<RoomMessagesViewModel>>(kind);

        RenderRoom();

        AssertSnackbars(shown ? [_messages] : []);
    }

    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    public void MediaPageLoad_ReportsEveryFailureExceptACancellation(string kind, bool shown)
    {
        _media = Make<OperationResult<List<RoomMediaItemViewModel>>>(kind);
        var cut = RenderRoom();

        cut.FindAll(".mud-tab").First(t => t.TextContent.Trim() == L["Media"].Value).Click();

        cut.WaitForAssertion(() => Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetMediaMetadataBatchAsync))));
        AssertSnackbars(shown ? [_media] : []);
    }
}
