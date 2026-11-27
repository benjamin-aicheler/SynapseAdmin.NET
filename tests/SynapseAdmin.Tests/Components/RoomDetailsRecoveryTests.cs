using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Room recovery on the room page (#587, AC-26): Make room admin and Join user open <see cref="RoomUserActionDialog"/>,
/// which names the shown room and is the confirmation; the page re-checks the room, sends the action, shows the result
/// and loads the room again after a success. The buttons' disabled state during a deletion or a purge is in
/// <c>RoomDetailsRoomDeletionTests</c>.
/// </summary>
public class RoomDetailsRecoveryTests : DetailPageTestBase
{
    private const string RoomA = "!a:hs";
    private const string RoomB = "!b:hs";
    private const string Admin = "@admin:hs";

    private readonly Stub _rooms;
    private TaskCompletionSource<DialogResult?> _dialogResult = Pending<DialogResult?>();
    private Func<Task<OperationResult>> _actionResult = () => Task.FromResult(OperationResult.Ok("Done"));

    public RoomDetailsRecoveryTests()
    {
        // The signed-in admin and the server, which the dialog shows and checks against.
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_SupportsAdminApi", _ => true)
            .On("get_ServerName", _ => "hs")
            .On("get_UserId", _ => Admin));
        Services.AddSingleton(Stub.Create<IMatrixSessionService>(s => s
            .On("get_Gateway", _ => gateway)
            .On("get_IsLoggedIn", _ => true)));

        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), args => Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(
                new RoomDetailViewModel
                {
                    RoomId = (string)args[0]!,
                    Name = NameOf((string)args[0]!),
                    Members = [new RoomMemberViewModel { UserId = "@carol:hs", Membership = "join" }, new RoomMemberViewModel { UserId = "@dave:other.org" }]
                })))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ =>
                Task.FromResult(OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel())))
            .On(nameof(IRoomService.MakeRoomAdminAsync), _ => _actionResult())
            .On(nameof(IRoomService.JoinUserToRoomAsync), _ => _actionResult());

        DialogStub.On(nameof(IDialogService.ShowAsync), _ => Task.FromResult(
            Stub.Create<IDialogReference>(s => s.On("get_Result", _ => _dialogResult.Task))));
    }

    private static string NameOf(string roomId) => roomId == RoomA ? "Alpha Lounge" : "Bravo Lounge";

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IReadOnlyList<object?[]> DetailLoads(string roomId) =>
        _rooms.CallsTo(nameof(IRoomService.GetRoomDetailsAsync)).Where(a => (string)a[0]! == roomId).ToList();

    private IReadOnlyList<object?[]> ActionCalls(string method) => _rooms.CallsTo(method);

    private void AssertSnackbars(params (string Message, Severity Severity)[] expected) =>
        Assert.Equal(expected, SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Select(a => ((string)a[0]!, (Severity)a[1]!)));

    private IRenderedComponent<RoomDetails> RenderRoom(string roomId = RoomA)
    {
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, roomId));
        cut.WaitForAssertion(() => Assert.Contains(NameOf(roomId), cut.Markup));
        return cut;
    }

    private static readonly string[] JoinServers = ["matrix.org", "other.org:8448"];

    /// <summary>What the dialog returns for bob: Join the user and the servers to join through (#606), Make admin the ID.</summary>
    private static object BobFor(string handler) => handler == "JoinUser"
        ? new RoomJoinRequestViewModel { Target = "@bob:hs", Servers = JoinServers }
        : "@bob:hs";

    public static TheoryData<string, RoomUserAction, string, string> Actions => new()
    {
        { "MakeRoomAdmin", RoomUserAction.MakeAdmin, "MakeRoomAdminTitle", nameof(IRoomService.MakeRoomAdminAsync) },
        { "JoinUser", RoomUserAction.Join, "JoinUserTitle", nameof(IRoomService.JoinUserToRoomAsync) },
    };

    [Theory, MemberData(nameof(Actions))]
    public async Task Button_OpensTheDialog_WithTheRoomTheAdminAndTheJoinedMembers(string handler, RoomUserAction action, string titleKey, string _)
    {
        var cut = RenderRoom();

        var running = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync))));

        var shown = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
        Assert.Equal(L[titleKey].Value, shown[0]);
        var parameters = (DialogParameters)shown[1]!;
        Assert.Equal(5, parameters.Count);
        Assert.Equal(action, parameters[nameof(RoomUserActionDialog.Action)]);
        Assert.Equal($"Alpha Lounge ({RoomA})", parameters[nameof(RoomUserActionDialog.RoomLabel)]);
        Assert.Equal(Admin, parameters[nameof(RoomUserActionDialog.SignedInUserId)]);
        Assert.Equal("hs", parameters[nameof(RoomUserActionDialog.ServerName)]);
        Assert.Equal(["@carol:hs", "@dave:other.org"], (IReadOnlyCollection<string>)parameters[nameof(RoomUserActionDialog.JoinedMemberIds)]!);
        AssertDialogOptions(DialogPresets.Small());

        _dialogResult.SetResult(DialogResult.Cancel());
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory, MemberData(nameof(Actions))]
    public async Task Success_SendsTheActionForTheShownRoom_ShowsTheResult_AndLoadsTheRoomAgain(string handler, RoomUserAction _, string __, string method)
    {
        var cut = RenderRoom();
        var running = InvokeHandler(cut, handler);

        _dialogResult.SetResult(DialogResult.Ok(BobFor(handler)));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        var call = Assert.Single(ActionCalls(method));
        Assert.Equal((RoomA, "@bob:hs"), ((string)call[0]!, (string?)call[1]));
        if (handler == "JoinUser") Assert.Same(JoinServers, call[2]);
        Assert.False(((CancellationToken)call[^1]!).IsCancellationRequested);
        AssertSnackbars(("Done", Severity.Success));
        Assert.Equal(2, DetailLoads(RoomA).Count);
    }

    /// <summary>An empty field is the signed-in admin: the page passes it on, and the service sends no user.</summary>
    [Fact]
    public async Task MakeRoomAdmin_ForTheSignedInAdmin_PassesTheEmptyId()
    {
        var cut = RenderRoom();
        var running = InvokeHandler(cut, "MakeRoomAdmin");

        _dialogResult.SetResult(DialogResult.Ok(""));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("", Assert.Single(ActionCalls(nameof(IRoomService.MakeRoomAdminAsync)))[1]);
    }

    /// <summary>A result that isn't the action's (the other action's type, or none) sends nothing.</summary>
    [Theory]
    [InlineData("MakeRoomAdmin", "JoinUser")]
    [InlineData("JoinUser", "MakeRoomAdmin")]
    [InlineData("JoinUser", null)]
    public async Task ResultOfAnotherType_SendsNothing(string handler, string? resultFor)
    {
        var cut = RenderRoom();
        var running = InvokeHandler(cut, handler);

        _dialogResult.SetResult(DialogResult.Ok(resultFor == null ? null : BobFor(resultFor)));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(ActionCalls(nameof(IRoomService.MakeRoomAdminAsync)));
        Assert.Empty(ActionCalls(nameof(IRoomService.JoinUserToRoomAsync)));
        AssertSnackbars();
    }

    /// <summary>A failure, a refusal ("not supported", a warning) and a cancellation don't load the room again.</summary>
    [Theory]
    [InlineData("MakeRoomAdmin", "failure")]
    [InlineData("JoinUser", "failure")]
    [InlineData("MakeRoomAdmin", "refused")]
    [InlineData("MakeRoomAdmin", "cancelled")]
    public async Task NoSuccess_IsReported_WithoutLoadingTheRoomAgain(string handler, string kind)
    {
        var result = kind == "refused"
            ? OperationResult.Failure(L["OperationNotSupportedByHomeserver"].Value, Severity.Warning)
            : Make<OperationResult>(kind);
        _actionResult = () => Task.FromResult(result);
        var cut = RenderRoom();
        var running = InvokeHandler(cut, handler);

        _dialogResult.SetResult(DialogResult.Ok(BobFor(handler)));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        if (kind == "cancelled") AssertSnackbars();
        else AssertSnackbars((result.Message!, result.Severity));
        Assert.Single(DetailLoads(RoomA));
    }

    [Theory, MemberData(nameof(Actions))]
    public async Task CancelledDialog_SendsNothing(string handler, RoomUserAction _, string __, string method)
    {
        var cut = RenderRoom();
        var running = InvokeHandler(cut, handler);

        _dialogResult.SetResult(DialogResult.Cancel());
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(ActionCalls(method));
        AssertSnackbars();
    }

    [Theory, MemberData(nameof(Actions))]
    public async Task RoomChangedWhileTheDialogIsOpen_Aborts(string handler, RoomUserAction _, string __, string method)
    {
        var cut = RenderRoom();
        var running = InvokeHandler(cut, handler);

        cut.Render(p => p.Add(x => x.RoomId, RoomB));
        cut.WaitForAssertion(() => Assert.Contains(NameOf(RoomB), cut.Markup));
        _dialogResult.SetResult(DialogResult.Ok(BobFor(handler)));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(ActionCalls(method));
        AssertSnackbars((L["SelectionChangedActionAborted"].Value, Severity.Warning));
    }

    /// <summary>The action ran for room A, so its result is shown; room B, shown by then, isn't loaded again for it.</summary>
    [Fact]
    public async Task RoomChangedWhileTheActionRuns_ShowsTheResult_WithoutLoadingAgain()
    {
        var answer = Pending<OperationResult>();
        _actionResult = () => answer.Task;
        var cut = RenderRoom();
        var running = InvokeHandler(cut, "JoinUser");
        _dialogResult.SetResult(DialogResult.Ok(BobFor("JoinUser")));
        cut.WaitForAssertion(() => Assert.Single(ActionCalls(nameof(IRoomService.JoinUserToRoomAsync))));

        cut.Render(p => p.Add(x => x.RoomId, RoomB));
        cut.WaitForAssertion(() => Assert.Contains(NameOf(RoomB), cut.Markup));
        answer.SetResult(OperationResult.Ok("Joined"));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        AssertSnackbars(("Joined", Severity.Success));
        Assert.Single(DetailLoads(RoomA));
        Assert.Single(DetailLoads(RoomB));
    }
}
