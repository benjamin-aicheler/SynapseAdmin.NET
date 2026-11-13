using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Issue #367: user actions (deactivation in particular) must target the user shown in the UI,
/// never a newer route parameter, and stale user data must never be shown.
/// </summary>
public class UserDetailsStaleSelectionTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";

    private readonly Stub _users;
    private readonly Dictionary<string, TaskCompletionSource<OperationResult<UserDetailViewModel>>> _pendingDetails = new();
    private readonly Dictionary<string, CancellationToken> _detailTokens = new();

    public UserDetailsStaleSelectionTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), args =>
            {
                var userId = (string)args[0]!;
                _detailTokens[userId] = (CancellationToken)args[1]!;
                if (_pendingDetails.TryGetValue(userId, out var pending)) return pending.Task;
                return Task.FromResult(Found(userId));
            })
            .On(nameof(IUserService.DeactivateUserAsync), _ => Task.FromResult(OperationResult.Ok()))
            .On(nameof(IUserService.QuarantineMediaAsync), _ => Task.FromResult(OperationResult.Ok()))
            .On(nameof(IUserService.LoginAsUserAsync), _ => Task.FromResult(OperationResult<string>.Failure("no")));
    }

    private static OperationResult<UserDetailViewModel> Found(string userId) =>
        OperationResult<UserDetailViewModel>.Ok(new UserDetailViewModel
        {
            UserId = userId,
            DisplayName = userId == UserA ? "Alice Example" : "Bob Example"
        });

    private IRenderedComponent<UserDetails> RenderUser(string userId) =>
        Render<UserDetails>(p => p.Add(x => x.UserId, userId));

    private static void Navigate(IRenderedComponent<UserDetails> cut, string userId) =>
        cut.Render(p => p.Add(x => x.UserId, userId));

    private IReadOnlyList<object?[]> DeactivateCalls => _users.CallsTo(nameof(IUserService.DeactivateUserAsync));

    [Fact]
    public async Task FailedLoadAfterNavigation_DoesNotShowPreviousUserOrAllowDeactivation()
    {
        var cut = RenderUser(UserA);
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));

        _pendingDetails[UserB] = Pending<OperationResult<UserDetailViewModel>>();
        Navigate(cut, UserB);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Alice Example", cut.Markup));

        _pendingDetails[UserB].SetResult(OperationResult<UserDetailViewModel>.Failure("User could not be loaded"));
        cut.WaitForAssertion(() => Assert.Contains("User could not be loaded", cut.Markup));
        Assert.DoesNotContain("Alice Example", cut.Markup);
        Assert.Null(GetField<UserDetailViewModel>(cut.Instance, "user"));

        await InvokeHandler(cut, "DeactivateUser");
        await InvokeHandler(cut, "QuarantineAllMedia");
        await InvokeHandler(cut, "LoginAsUser");
        Assert.Empty(Confirmations);
        Assert.Empty(DeactivateCalls);
        Assert.Empty(_users.CallsTo(nameof(IUserService.QuarantineMediaAsync)));
        Assert.Empty(_users.CallsTo(nameof(IUserService.LoginAsUserAsync)));
    }

    [Fact]
    public async Task ReversedResponseOrder_OlderResponseDoesNotOverwriteNewerUser()
    {
        _pendingDetails[UserA] = Pending<OperationResult<UserDetailViewModel>>();
        _pendingDetails[UserB] = Pending<OperationResult<UserDetailViewModel>>();

        var cut = RenderUser(UserA);
        Navigate(cut, UserB);
        cut.WaitForAssertion(() => Assert.True(_detailTokens.ContainsKey(UserB)));
        Assert.True(_detailTokens[UserA].IsCancellationRequested);

        _pendingDetails[UserB].SetResult(Found(UserB));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));

        _pendingDetails[UserA].SetResult(Found(UserA));
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));
        Assert.DoesNotContain("Alice Example", cut.Markup);

        var action = InvokeHandler(cut, "DeactivateUser");
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Assert.Contains(UserB, Confirmations[0].Message);
        Confirmations[0].Result.SetResult(true);
        await action;

        Assert.Equal(UserB, Assert.Single(DeactivateCalls)[0]);
    }

    [Theory]
    [InlineData("DeactivateUser")]
    [InlineData("QuarantineAllMedia")]
    public async Task NavigationDuringConfirmation_AbortsActionInsteadOfRetargeting(string handler)
    {
        var cut = RenderUser(UserA);
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));

        var action = InvokeHandler(cut, handler);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Assert.Contains("Alice Example", Confirmations[0].Message);
        Assert.Contains(UserA, Confirmations[0].Message);

        Navigate(cut, UserB);
        cut.WaitForAssertion(() => Assert.Contains("Bob Example", cut.Markup));

        Confirmations[0].Result.SetResult(true);
        await action;

        Assert.Empty(DeactivateCalls);
        Assert.Empty(_users.CallsTo(nameof(IUserService.QuarantineMediaAsync)));
        Assert.True(WarningShown());
    }

    [Fact]
    public async Task ConfirmedDeactivation_TargetsDisplayedUser()
    {
        var cut = RenderUser(UserA);
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));

        var action = InvokeHandler(cut, "DeactivateUser");
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;

        Assert.Equal(UserA, Assert.Single(DeactivateCalls)[0]);
        Assert.False(WarningShown());
    }
}
