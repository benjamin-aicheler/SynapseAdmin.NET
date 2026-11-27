using System.Net;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// "Redact events" on the user page (#600, AC-25): the dialog gets the user and the memberships, the confirmation names
/// the user and the rooms, a changed user aborts, and the page shows a banner while it follows the redaction, also
/// after reopening the user, until it reports how it ended.
/// </summary>
public class UserDetailsRedactionTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";
    private const string LabelA = "Alice Example (@alice:hs)";
    private const string Started = "Redaction started";

    private static readonly List<UserMembershipViewModel> MembershipsA =
    [
        new() { RoomId = "!joined:hs", Membership = "join" },
        new() { RoomId = "!left:hs", Membership = "leave" },
    ];

    private readonly Stub _redactions;
    private readonly Dictionary<string, string> _tracked = [];
    private OperationResult<string> _startResult = OperationResult<string>.Ok("redact-1", Started);
    private Func<string, Task<OperationResult<UserRedactionStatusViewModel>>> _status = _ => Pending<OperationResult<UserRedactionStatusViewModel>>().Task;
    private TaskCompletionSource<DialogResult?> _dialogResult = Pending<DialogResult?>();

    public UserDetailsRedactionTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.GetUserDetailsAsync), args => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                (string)args[0]! == UserA
                    ? new UserDetailViewModel { UserId = UserA, DisplayName = "Alice Example", Memberships = MembershipsA }
                    : new UserDetailViewModel { UserId = UserB, DisplayName = "Bob Example" })))));
        Services.AddSingleton(Stub.Create<IUserRedactionService>(out _redactions));
        _redactions
            .On(nameof(IUserRedactionService.StartRedactionAsync), _ => Task.FromResult(_startResult))
            .On(nameof(IUserRedactionService.GetRedactionStatusAsync), args => _status((string)args[0]!))
            .On(nameof(IUserRedactionService.GetActiveRedactionId), args => _tracked.GetValueOrDefault((string)args[0]!));
        DialogStub.On(nameof(IDialogService.ShowAsync), _ => Task.FromResult(
            Stub.Create<IDialogReference>(s => s.On("get_Result", _ => _dialogResult.Task))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();
    private IReadOnlyList<object?[]> StartCalls => _redactions.CallsTo(nameof(IUserRedactionService.StartRedactionAsync));
    private IReadOnlyList<object?[]> StatusCalls => _redactions.CallsTo(nameof(IUserRedactionService.GetRedactionStatusAsync));
    private IReadOnlyList<object?[]> ClearCalls => _redactions.CallsTo(nameof(IUserRedactionService.ClearActiveRedactionId));
    private IReadOnlyList<object?[]> SnackbarCalls => SnackbarStub.CallsTo(nameof(ISnackbar.Add));
    private string Banner => L["UserRedactionInProgress"].Value;

    private static string Markup(IRenderedComponent<UserDetails> cut) => WebUtility.HtmlDecode(cut.Markup);

    private static OperationResult<UserRedactionStatusViewModel> Status(UserRedactionState state, int failures = 0, string redactId = "redact-1") =>
        OperationResult<UserRedactionStatusViewModel>.Ok(new() { RedactId = redactId, State = state, FailedRedactionCount = failures });

    private IRenderedComponent<UserDetails> RenderUser(string userId = UserA)
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, userId));
        cut.WaitForAssertion(() => Assert.Contains(userId == UserA ? "Alice Example" : "Bob Example", cut.Markup));
        return cut;
    }

    private static void Navigate(IRenderedComponent<UserDetails> cut, string userId)
    {
        cut.Render(p => p.Add(x => x.UserId, userId));
        cut.WaitForAssertion(() => Assert.Contains(userId == UserA ? "Alice Example" : "Bob Example", cut.Markup));
    }

    private IElement RedactButton(IRenderedComponent<UserDetails> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == L["RedactUserEvents"].Value);

    /// <summary>Opens the dialog, returns <paramref name="request"/> from it, and waits for the confirmation.</summary>
    private (Task Action, (string Message, TaskCompletionSource<bool?> Result) Confirmation) Choose(
        IRenderedComponent<UserDetails> cut, UserRedactionRequestViewModel request)
    {
        var action = InvokeHandler(cut, "OpenRedactEventsDialog");
        _dialogResult.SetResult(DialogResult.Ok(request));
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        return (action, Confirmations[0]);
    }

    private async Task StartAsync(IRenderedComponent<UserDetails> cut, UserRedactionRequestViewModel? request = null)
    {
        var (action, confirmation) = Choose(cut, request ?? new UserRedactionRequestViewModel());
        confirmation.Result.SetResult(true);
        await action.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private void AssertSnackbars(params (string Message, Severity Severity)[] expected) =>
        Assert.Equal(expected, SnackbarCalls.Select(a => ((string)a[0]!, (Severity)a[1]!)));

    // ---- Dialog and confirmation ---------------------------------------------------------------------

    [Fact]
    public async Task Button_OpensTheDialog_WithTheUserAndTheMemberships()
    {
        var cut = RenderUser();

        RedactButton(cut).Click();
        _dialogResult.SetResult(DialogResult.Cancel());

        var args = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
        Assert.Equal(L["RedactUserEvents"].Value, args[0]);
        var parameters = (DialogParameters)args[1]!;
        Assert.Equal(LabelA, parameters[nameof(RedactUserEventsDialog.Target)]);
        Assert.Same(MembershipsA, parameters[nameof(RedactUserEventsDialog.Memberships)]);
        AssertDialogOptions(new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true });
        await Task.Yield();
        Assert.Empty(Confirmations);
        Assert.Empty(StartCalls);
    }

    [Fact]
    public async Task Confirmation_NamesTheUser_TheUserRooms_AndThatItCantBeUndone()
    {
        var cut = RenderUser();

        var (action, confirmation) = Choose(cut, new UserRedactionRequestViewModel());

        Assert.Equal($"{L["RedactUserEventsConfirmation", LabelA, L["RedactScopeUserRooms"]]} {L["RedactUserEventsHint"]}", confirmation.Message);
        confirmation.Result.SetResult(false);
        await action.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(StartCalls);
        Assert.DoesNotContain(Banner, Markup(cut));
    }

    [Fact]
    public async Task Confirmation_NamesTheNumberOfSelectedRooms()
    {
        var cut = RenderUser();

        var (action, confirmation) = Choose(cut, new UserRedactionRequestViewModel { AllRooms = false, RoomIds = ["!joined:hs", "!left:hs"] });

        Assert.Equal($"{L["RedactUserEventsConfirmation", LabelA, L["RedactScopeSelectedRooms", 2]]} {L["RedactUserEventsHint"]}", confirmation.Message);
        confirmation.Result.SetResult(null);
        await action.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(StartCalls);
    }

    [Fact]
    public async Task UserChangedWhileTheDialogIsOpen_Aborts()
    {
        var cut = RenderUser();
        var action = InvokeHandler(cut, "OpenRedactEventsDialog");

        Navigate(cut, UserB);
        _dialogResult.SetResult(DialogResult.Ok(new UserRedactionRequestViewModel()));
        await action.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(Confirmations);
        Assert.Empty(StartCalls);
        AssertSnackbars((L["SelectionChangedActionAborted"].Value, Severity.Warning));
    }

    [Fact]
    public async Task UserChangedWhileTheConfirmationIsOpen_Aborts()
    {
        var cut = RenderUser();
        var (action, confirmation) = Choose(cut, new UserRedactionRequestViewModel());

        Navigate(cut, UserB);
        confirmation.Result.SetResult(true);
        await action.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(StartCalls);
        AssertSnackbars((L["SelectionChangedActionAborted"].Value, Severity.Warning));
    }

    // ---- Starting ------------------------------------------------------------------------------------

    [Fact]
    public async Task Confirmed_StartsTheRedactionForTheUser_ShowsTheBanner_AndChecksTheStatus()
    {
        var cut = RenderUser();
        var request = new UserRedactionRequestViewModel { Reason = "spam" };

        await StartAsync(cut, request);

        var args = Assert.Single(StartCalls);
        Assert.Equal(UserA, args[0]);
        Assert.Same(request, args[1]);
        AssertSnackbars((Started, Severity.Success));
        cut.WaitForAssertion(() => Assert.Contains(Banner, Markup(cut)));
        Assert.Equal(L["UserRedactionStatusScheduled"].Value, cut.Find(".redaction-state").TextContent.Trim());
        Assert.True(RedactButton(cut).HasAttribute("disabled"));
        cut.WaitForAssertion(() => Assert.Equal("redact-1", Assert.Single(StatusCalls)[0]));
    }

    [Fact]
    public async Task StartWithoutARedactId_ShowsTheResult_WithoutABanner()
    {
        _startResult = OperationResult<string>.Ok(string.Empty, Started);
        var cut = RenderUser();

        await StartAsync(cut);

        AssertSnackbars((Started, Severity.Success));
        cut.Render();
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(StatusCalls);
        Assert.False(RedactButton(cut).HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("OperationNotSupportedByHomeserver", Severity.Warning)]
    [InlineData("ErrorRedactingUserEvents", Severity.Error)]
    [InlineData("UserRedactionNoRoomsSelected", Severity.Warning)]
    public async Task StartFails_ShowsTheResult_WithoutABanner(string key, Severity severity)
    {
        _startResult = OperationResult<string>.Failure(L[key], severity);
        var cut = RenderUser();

        await StartAsync(cut);

        AssertSnackbars((L[key].Value, severity));
        cut.Render();
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(StatusCalls);
    }

    [Fact]
    public async Task UserChangedWhileStarting_DoesNotFollowTheRedaction()
    {
        var start = Pending<OperationResult<string>>();
        _redactions.On(nameof(IUserRedactionService.StartRedactionAsync), _ => start.Task);
        var cut = RenderUser();
        var (action, confirmation) = Choose(cut, new UserRedactionRequestViewModel());
        confirmation.Result.SetResult(true);
        cut.WaitForAssertion(() => Assert.Single(StartCalls));

        Navigate(cut, UserB);
        start.SetResult(_startResult);
        await action.WaitAsync(TimeSpan.FromSeconds(10));

        AssertSnackbars((Started, Severity.Success));
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(StatusCalls);
    }

    // ---- Following the status ------------------------------------------------------------------------

    [Fact]
    public async Task Active_KeepsTheBanner_AndShowsTheState()
    {
        _status = _ => Task.FromResult(Status(UserRedactionState.Active));
        var cut = RenderUser();

        await StartAsync(cut);

        cut.WaitForAssertion(() => Assert.Equal(L["UserRedactionStatusActive"].Value, cut.Find(".redaction-state").TextContent.Trim()));
        Assert.Contains(Banner, Markup(cut));
        Assert.Empty(ClearCalls);
        AssertSnackbars((Started, Severity.Success));
    }

    /// <summary>An ended redaction removes the banner, stops being tracked and reports a localized result.</summary>
    [Theory]
    [InlineData(UserRedactionState.Complete, 0, "UserRedactionCompleted", Severity.Success)]
    [InlineData(UserRedactionState.Complete, 3, "UserRedactionCompletedWithFailures", Severity.Warning)]
    [InlineData(UserRedactionState.Failed, 0, "UserRedactionFailed", Severity.Error)]
    [InlineData(UserRedactionState.Unknown, 0, "UserRedactionStatusUnknown", Severity.Warning)]
    public async Task Ended_RemovesTheBanner_ClearsTheTracking_AndReportsIt(UserRedactionState state, int failures, string key, Severity severity)
    {
        _status = _ => Task.FromResult(Status(state, failures));
        var cut = RenderUser();

        await StartAsync(cut);

        cut.WaitForAssertion(() => Assert.Equal(2, SnackbarCalls.Count));
        AssertSnackbars((Started, Severity.Success), (failures > 0 ? L[key, failures].Value : L[key].Value, severity));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Banner, Markup(cut)));
        Assert.Equal([UserA, "redact-1"], Assert.Single(ClearCalls));
        Assert.Single(StatusCalls);
        Assert.False(RedactButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ThrowingStatusRequest_RemovesTheBanner_LogsIt_AndReportsTheError()
    {
        var logger = new RecordingLogger<UserDetails>();
        Services.AddSingleton<ILogger<UserDetails>>(logger);
        var error = new InvalidOperationException("boom");
        _status = _ => Task.FromException<OperationResult<UserRedactionStatusViewModel>>(error);
        var cut = RenderUser();

        await StartAsync(cut);

        cut.WaitForAssertion(() => Assert.Equal(2, SnackbarCalls.Count));
        AssertSnackbars((Started, Severity.Success), (L["ErrorFetchingUserRedactionStatus"].Value, Severity.Error));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Banner, Markup(cut)));
        Assert.Equal([UserA, "redact-1"], Assert.Single(ClearCalls));
        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("Unexpected error while following redaction {RedactId} of user {UserId}", entry.Template);
        Assert.Same(error, entry.Exception);
    }

    /// <summary>
    /// Failed status requests are retried every 5 s; the fifth failure in a row removes the banner and reports it.
    /// Takes about 20 s, because the poll interval is a constant.
    /// </summary>
    [Fact]
    public async Task FiveFailedStatusRequestsInARow_AreLogged_ThenRemoveTheBannerAndReportTheError()
    {
        var logger = new RecordingLogger<UserDetails>();
        Services.AddSingleton<ILogger<UserDetails>>(logger);
        _status = _ => Task.FromResult(OperationResult<UserRedactionStatusViewModel>.Failure("status unavailable"));
        var cut = RenderUser();

        await StartAsync(cut);

        cut.WaitForAssertion(() => Assert.Equal(2, SnackbarCalls.Count), TimeSpan.FromSeconds(30));
        Assert.Equal(5, StatusCalls.Count);
        AssertSnackbars((Started, Severity.Success), (L["ErrorFetchingUserRedactionStatus"].Value, Severity.Error));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Banner, Markup(cut)));
        Assert.Equal([UserA, "redact-1"], Assert.Single(ClearCalls));
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.All(warnings, w => Assert.Equal(
            "Failed to get the status of redaction {RedactId} of user {UserId}. Attempt {Attempt} of {MaxAttempts}. Error: {Error}", w.Template));
        Assert.Equal([1, 2, 3, 4, 5], warnings.Select(w => w.Arguments["Attempt"]));
    }

    // ---- Reopening and moving between users ----------------------------------------------------------

    [Fact]
    public void TrackedRedaction_IsShownWhenTheUserOpens_AndChecked()
    {
        _tracked[UserA] = "redact-7";

        var cut = RenderUser();

        cut.WaitForAssertion(() => Assert.Contains(Banner, Markup(cut)));
        Assert.Empty(cut.FindAll(".redaction-state"));
        Assert.True(RedactButton(cut).HasAttribute("disabled"));
        cut.WaitForAssertion(() => Assert.Equal("redact-7", Assert.Single(StatusCalls)[0]));
    }

    [Fact]
    public void RedactionOfAnotherUser_IsNotShown()
    {
        _tracked[UserB] = "redact-7";

        var cut = RenderUser();

        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(StatusCalls);
        Assert.False(RedactButton(cut).HasAttribute("disabled"));
    }

    /// <summary>
    /// Moving to another user stops following the redaction but keeps it tracked: it still runs on the homeserver, and
    /// a late answer for it changes nothing.
    /// </summary>
    [Fact]
    public async Task MovingToAnotherUser_StopsFollowing_KeepsTheTracking_AndIgnoresALateAnswer()
    {
        var answer = Pending<OperationResult<UserRedactionStatusViewModel>>();
        CancellationToken pollToken = default;
        _redactions.On(nameof(IUserRedactionService.GetRedactionStatusAsync), args =>
        {
            pollToken = (CancellationToken)args[1]!;
            return answer.Task;
        });
        _tracked[UserA] = "redact-7";
        var cut = RenderUser();
        cut.WaitForAssertion(() => Assert.Single(StatusCalls));

        Navigate(cut, UserB);
        answer.SetResult(Status(UserRedactionState.Complete, redactId: "redact-7"));
        await Task.Delay(100);

        Assert.True(pollToken.IsCancellationRequested);
        cut.Render();
        Assert.DoesNotContain(Banner, Markup(cut));
        Assert.Empty(ClearCalls);
        Assert.Empty(SnackbarCalls);
        Assert.False(RedactButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void ReturningToTheUser_ShowsTheTrackedRedactionAgain()
    {
        _tracked[UserA] = "redact-7";
        var cut = RenderUser();
        cut.WaitForAssertion(() => Assert.Single(StatusCalls));

        Navigate(cut, UserB);
        Navigate(cut, UserA);

        cut.WaitForAssertion(() => Assert.Contains(Banner, Markup(cut)));
        cut.WaitForAssertion(() => Assert.Equal(2, StatusCalls.Count));
    }

    [Fact]
    public async Task Disposing_StopsFollowing()
    {
        CancellationToken pollToken = default;
        _redactions.On(nameof(IUserRedactionService.GetRedactionStatusAsync), args =>
        {
            pollToken = (CancellationToken)args[1]!;
            return Pending<OperationResult<UserRedactionStatusViewModel>>().Task;
        });
        _tracked[UserA] = "redact-7";
        var cut = RenderUser();
        cut.WaitForAssertion(() => Assert.Single(StatusCalls));

        await cut.InvokeAsync(() => cut.Instance.Dispose());

        Assert.True(pollToken.IsCancellationRequested);
        Assert.Empty(ClearCalls);
    }
}
