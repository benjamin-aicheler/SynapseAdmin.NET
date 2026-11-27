using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// "Join to room" on the user page (#605, AC-27): disabled for deactivated and suspended accounts; it opens
/// <see cref="UserJoinRoomDialog"/> for the shown user, which is the confirmation; the page re-checks the user, joins it
/// to the chosen room through the room service, shows the result and loads the user again after a success.
/// </summary>
public class UserDetailsJoinRoomTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";
    private const string Room = "!left:hs";

    private readonly Stub _users;
    private readonly Stub _rooms;
    private readonly UserDetailViewModel _shown = new()
    {
        UserId = UserA,
        DisplayName = "Alice Example",
        Memberships = [new() { RoomId = "!joined:hs", Membership = "join" }, new() { RoomId = Room, Membership = "leave" }]
    };
    private static readonly string[] Servers = ["matrix.org"];
    private static readonly RoomJoinRequestViewModel Request = new() { Target = Room, Servers = Servers };
    private TaskCompletionSource<DialogResult?> _dialogResult = Pending<DialogResult?>();
    private Func<Task<OperationResult>> _joinResult = () => Task.FromResult(OperationResult.Ok("Joined"));

    public UserDetailsJoinRoomTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), args => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                (string)args[0]! == UserA ? _shown : new UserDetailViewModel { UserId = UserB, DisplayName = "Bob Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())));

        // Replaces the base class's default room service stub, to record the join.
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms.On(nameof(IRoomService.JoinUserToRoomAsync), _ => _joinResult());

        DialogStub.On(nameof(IDialogService.ShowAsync), _ => Task.FromResult(
            Stub.Create<IDialogReference>(s => s.On("get_Result", _ => _dialogResult.Task))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private int DetailReads(string userId) =>
        _users.CallsTo(nameof(IUserService.GetUserDetailsAsync)).Count(a => (string)a[0]! == userId);

    private IReadOnlyList<object?[]> JoinCalls => _rooms.CallsTo(nameof(IRoomService.JoinUserToRoomAsync));

    private void AssertSnackbars(params (string Message, Severity Severity)[] expected) =>
        Assert.Equal(expected, SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Select(a => ((string)a[0]!, (Severity)a[1]!)));

    private IRenderedComponent<UserDetails> RenderUser(string userId = UserA)
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, userId));
        cut.WaitForAssertion(() => Assert.Contains(userId == UserA ? "Alice Example" : "Bob Example", cut.Markup));
        return cut;
    }

    private AngleSharp.Dom.IElement JoinButton(IRenderedComponent<UserDetails> cut) =>
        Assert.Single(cut.FindAll("button"), b => WebUtility.HtmlDecode(b.TextContent).Trim() == L["JoinToRoom"].Value);

    [Fact]
    public void Button_IsEnabled_ForAnActiveAccount()
    {
        var cut = RenderUser();

        Assert.False(JoinButton(cut).HasAttribute("disabled"));
    }

    /// <summary>Synapse refuses to join a suspended account; nobody can use a deactivated one.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DeactivatedOrSuspendedAccount_DisablesTheButton_AndTheHandlerShowsNothing(bool deactivated, bool suspended)
    {
        _shown.Deactivated = deactivated;
        _shown.Suspended = suspended;
        var cut = RenderUser();

        Assert.True(JoinButton(cut).HasAttribute("disabled"));
        await InvokeHandler(cut, "OpenJoinRoomDialog").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
    }

    [Fact]
    public async Task Button_OpensTheDialog_WithTheUserAndTheMemberships()
    {
        var cut = RenderUser();

        var running = InvokeHandler(cut, "OpenJoinRoomDialog");
        cut.WaitForAssertion(() => Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync))));

        var shown = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
        Assert.Equal(L["JoinToRoomTitle", $"Alice Example ({UserA})"].Value, shown[0]);
        var parameters = (DialogParameters)shown[1]!;
        Assert.Equal(2, parameters.Count);
        Assert.Equal($"Alice Example ({UserA})", parameters[nameof(UserJoinRoomDialog.UserLabel)]);
        Assert.Same(_shown.Memberships, parameters[nameof(UserJoinRoomDialog.Memberships)]);
        AssertDialogOptions(DialogPresets.Small());

        _dialogResult.SetResult(DialogResult.Cancel());
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Success_JoinsTheShownUser_ShowsTheResult_AndLoadsTheUserAgain()
    {
        var cut = RenderUser();
        var running = InvokeHandler(cut, "OpenJoinRoomDialog");

        _dialogResult.SetResult(DialogResult.Ok(Request));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        var call = Assert.Single(JoinCalls);
        Assert.Equal((Room, UserA), ((string)call[0]!, (string)call[1]!));
        Assert.Same(Servers, call[2]);
        Assert.False(((CancellationToken)call[^1]!).IsCancellationRequested);
        AssertSnackbars(("Joined", Severity.Success));
        Assert.Equal(2, DetailReads(UserA));
    }

    /// <summary>A failure, a refusal ("not supported", a warning) and a cancellation don't load the user again.</summary>
    [Theory]
    [InlineData("failure")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task NoSuccess_IsReported_WithoutLoadingTheUserAgain(string kind)
    {
        var result = kind == "refused"
            ? OperationResult.Failure(L["OperationNotSupportedByHomeserver"].Value, Severity.Warning)
            : Make<OperationResult>(kind);
        _joinResult = () => Task.FromResult(result);
        var cut = RenderUser();
        var running = InvokeHandler(cut, "OpenJoinRoomDialog");

        _dialogResult.SetResult(DialogResult.Ok(Request));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        if (kind == "cancelled") AssertSnackbars();
        else AssertSnackbars((result.Message!, result.Severity));
        Assert.Equal(1, DetailReads(UserA));
    }

    /// <summary>The dialog returns a <see cref="RoomJoinRequestViewModel"/> (#606); anything else sends nothing.</summary>
    [Fact]
    public async Task ResultOfAnotherType_SendsNothing()
    {
        var cut = RenderUser();
        var running = InvokeHandler(cut, "OpenJoinRoomDialog");

        _dialogResult.SetResult(DialogResult.Ok(Room));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(JoinCalls);
        AssertSnackbars();
    }

    [Fact]
    public async Task CancelledDialog_SendsNothing()
    {
        var cut = RenderUser();
        var running = InvokeHandler(cut, "OpenJoinRoomDialog");

        _dialogResult.SetResult(DialogResult.Cancel());
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(JoinCalls);
        AssertSnackbars();
    }

    [Fact]
    public async Task UserChangedWhileTheDialogIsOpen_Aborts()
    {
        var cut = RenderUser();
        var running = InvokeHandler(cut, "OpenJoinRoomDialog");

        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));
        _dialogResult.SetResult(DialogResult.Ok(Request));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(JoinCalls);
        AssertSnackbars((L["SelectionChangedActionAborted"].Value, Severity.Warning));
    }

    /// <summary>The join ran for user A, so its result is shown; user B, shown by then, isn't loaded again for it.</summary>
    [Fact]
    public async Task UserChangedWhileTheJoinRuns_ShowsTheResult_WithoutLoadingAgain()
    {
        var answer = Pending<OperationResult>();
        _joinResult = () => answer.Task;
        var cut = RenderUser();
        var running = InvokeHandler(cut, "OpenJoinRoomDialog");
        _dialogResult.SetResult(DialogResult.Ok(Request));
        cut.WaitForAssertion(() => Assert.Single(JoinCalls));

        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));
        answer.SetResult(OperationResult.Ok("Joined"));
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        AssertSnackbars(("Joined", Severity.Success));
        Assert.Equal(1, DetailReads(UserA));
        Assert.Equal(1, DetailReads(UserB));
    }
}
