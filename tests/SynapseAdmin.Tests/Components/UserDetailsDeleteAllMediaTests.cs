using System.Net;
using AngleSharp.Dom;
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
/// "Delete all media" on the user page (#599, AC-24): the confirmation names the user, the number of uploads if known
/// and what is deleted; a changed user aborts; while it runs, the page shows the count with Stop and disables the other
/// media actions; afterwards it shows the result and reads the media again.
/// </summary>
public class UserDetailsDeleteAllMediaTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";
    private const string LabelA = "Alice Example (@alice:hs)";

    private readonly Stub _users;
    private TaskCompletionSource<OperationResult<UserMediaDeletionViewModel>> _deletion = Pending<OperationResult<UserMediaDeletionViewModel>>();
    private IProgress<int>? _progress;
    private CancellationToken _token;

    private static readonly OperationResult<UserMediaDeletionViewModel> Done = new()
    {
        Success = true, Message = "All gone", Severity = Severity.Success,
        Data = new UserMediaDeletionViewModel { Deleted = 42, Remaining = 0, Outcome = UserMediaDeletionOutcome.AllDeleted }
    };

    public UserDetailsDeleteAllMediaTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), args => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                (string)args[0]! == UserA
                    ? new UserDetailViewModel { UserId = UserA, DisplayName = "Alice Example" }
                    : new UserDetailViewModel { UserId = UserB, DisplayName = "Bob Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(
                OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel
                {
                    TotalCount = 40,
                    Items = [new UserMediaItemViewModel { MediaId = "media1", MediaType = "application/pdf", CreatedTimestamp = 1_700_000_000_000 }]
                })))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel { Count = 42, Bytes = 1000 })))
            .On(nameof(IUserService.DeleteAllMediaAsync), args =>
            {
                _progress = (IProgress<int>?)args[1];
                _token = (CancellationToken)args[2]!;
                return _deletion.Task;
            });
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IRenderedComponent<UserDetails> RenderUser()
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, UserA));
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));
        return cut;
    }

    private void OpenMediaTab(IRenderedComponent<UserDetails> cut)
    {
        cut.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == L["Media"].Value).Click();
        cut.WaitForAssertion(() => Assert.NotNull(GetField<UserMediaTotalsViewModel>(cut.Instance, "_mediaTotals")));
    }

    private IElement Button(IRenderedComponent<UserDetails> cut, string key) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == L[key].Value);

    private int MediaPageReads => _users.CallsTo(nameof(IUserService.GetUserMediaPageAsync)).Count;

    private (string Message, TaskCompletionSource<bool?> Result) Confirm(IRenderedComponent<UserDetails> cut)
    {
        Button(cut, "DeleteAllMedia").Click();
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        return Confirmations[0];
    }

    [Fact]
    public void Confirmation_NamesTheUserAndWhatIsDeleted_WithoutACount_BeforeTheMediaTabLoads()
    {
        var cut = RenderUser();

        var (message, result) = Confirm(cut);

        Assert.Equal($"{L["DeleteAllUserMediaConfirmationNoCount", LabelA]} {L["DeleteAllUserMediaHint"]}", message);
        Assert.Equal(L["DeleteAllMedia"].Value, Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowMessageBoxAsync)))[0]);
        result.SetResult(false);
    }

    [Fact]
    public void Confirmation_NamesTheNumberOfUploads_OnceTheTotalsAreKnown()
    {
        var cut = RenderUser();
        OpenMediaTab(cut);

        var (message, result) = Confirm(cut);

        Assert.Equal($"{L["DeleteAllUserMediaConfirmation", LabelA, 42L]} {L["DeleteAllUserMediaHint"]}", message);
        result.SetResult(false);
    }

    [Fact]
    public void NotConfirmed_DeletesNothing()
    {
        var cut = RenderUser();

        Confirm(cut).Result.SetResult(false);

        cut.WaitForAssertion(() => Assert.False(Button(cut, "DeleteAllMedia").HasAttribute("disabled")));
        Assert.Empty(_users.CallsTo(nameof(IUserService.DeleteAllMediaAsync)));
    }

    [Fact]
    public void AnotherUserWhileConfirming_AbortsTheDeletion()
    {
        var cut = RenderUser();
        var (_, result) = Confirm(cut);

        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));
        result.SetResult(true);

        cut.WaitForAssertion(() => Assert.True(WarningShown()));
        Assert.Empty(_users.CallsTo(nameof(IUserService.DeleteAllMediaAsync)));
    }

    [Fact]
    public void Running_ShowsTheCount_AndDisablesTheMediaActions_ThenShowsTheResult_AndReloadsTheMedia()
    {
        var cut = RenderUser();
        OpenMediaTab(cut);
        var readsBefore = MediaPageReads;

        Confirm(cut).Result.SetResult(true);
        cut.WaitForAssertion(() => Assert.NotNull(_progress));
        Assert.Equal(UserA, _users.CallsTo(nameof(IUserService.DeleteAllMediaAsync)).Single()[0]);
        cut.InvokeAsync(() => _progress!.Report(150));

        cut.WaitForAssertion(() => Assert.Contains(L["DeletingUserMedia", LabelA, 150].Value, WebUtility.HtmlDecode(cut.Markup)));
        Assert.True(Button(cut, "DeleteAllMedia").HasAttribute("disabled"));
        Assert.True(Button(cut, "QuarantineMedia").HasAttribute("disabled"));
        var itemActions = cut.FindAll("tbody button").Where(b => b.GetAttribute("title") is { } t
            && (t == L["Quarantine"].Value || t == L["Protect"].Value || t == L["Delete"].Value)).ToList();
        Assert.Equal(3, itemActions.Count);
        Assert.All(itemActions, b => Assert.True(b.HasAttribute("disabled")));

        cut.InvokeAsync(() => _deletion.SetResult(Done));

        cut.WaitForAssertion(() => Assert.DoesNotContain(L["DeletingUserMedia", LabelA, 150].Value, WebUtility.HtmlDecode(cut.Markup)));
        Assert.False(Button(cut, "DeleteAllMedia").HasAttribute("disabled"));
        AssertSnackbars(Done);
        cut.WaitForAssertion(() => Assert.True(MediaPageReads > readsBefore));
        Assert.Equal(2, _users.CallsTo(nameof(IUserService.GetUserMediaTotalsAsync)).Count);
    }

    [Fact]
    public void Stop_CancelsTheRunningDeletion()
    {
        var cut = RenderUser();
        Confirm(cut).Result.SetResult(true);
        cut.WaitForAssertion(() => Assert.NotNull(_progress));
        Assert.False(_token.IsCancellationRequested);

        Button(cut, "Stop").Click();

        Assert.True(_token.IsCancellationRequested);
        var stopped = new OperationResult<UserMediaDeletionViewModel> { Success = false, Message = "Stopped", Severity = Severity.Warning };
        cut.InvokeAsync(() => _deletion.SetResult(stopped));
        cut.WaitForAssertion(() => AssertSnackbars(stopped));
    }

    /// <summary>The deletion was confirmed for Alice: moving to Bob neither stops it nor reloads Bob's media for it.</summary>
    [Fact]
    public void AnotherUserWhileRunning_KeepsItRunning_AndNamesTheConfirmedUser()
    {
        var cut = RenderUser();
        Confirm(cut).Result.SetResult(true);
        cut.WaitForAssertion(() => Assert.NotNull(_progress));

        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));

        Assert.False(_token.IsCancellationRequested);
        Assert.Contains(L["DeletingUserMedia", LabelA, 0].Value, WebUtility.HtmlDecode(cut.Markup));
        var readsBefore = MediaPageReads;
        cut.InvokeAsync(() => _deletion.SetResult(Done));
        cut.WaitForAssertion(() => AssertSnackbars(Done));
        Assert.Equal(readsBefore, MediaPageReads);
    }

    [Fact]
    public async Task ClosingThePage_CancelsTheRunningDeletion()
    {
        var cut = RenderUser();
        Confirm(cut).Result.SetResult(true);
        cut.WaitForAssertion(() => Assert.NotNull(_progress));

        await cut.InvokeAsync(() => cut.Instance.Dispose());

        Assert.True(_token.IsCancellationRequested);
        _deletion.SetResult(OperationResult<UserMediaDeletionViewModel>.Cancelled());
    }
}

