using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which results the background updates page reports in a snackbar: a successful pause/resume or job start, and every
/// failure except a silent cancellation.
/// </summary>
public class BackgroundUpdatesSnackbarTests : DetailPageTestBase
{
    private readonly Stub _updates;
    private OperationResult<BackgroundUpdatesStatusViewModel> _status =
        OperationResult<BackgroundUpdatesStatusViewModel>.Ok(new BackgroundUpdatesStatusViewModel { Enabled = true });
    private OperationResult<bool> _enabled = new();
    private OperationResult _started = new();

    public BackgroundUpdatesSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IBackgroundUpdatesService>(out _updates));
        _updates
            .On(nameof(IBackgroundUpdatesService.GetStatusAsync), _ => Task.FromResult(_status))
            .On(nameof(IBackgroundUpdatesService.SetEnabledAsync), _ => Task.FromResult(_enabled))
            .On(nameof(IBackgroundUpdatesService.StartJobAsync), _ => Task.FromResult(_started));
    }

    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", false)]
    public void StatusLoad_ReportsEveryFailureExceptACancellation(string kind, bool shown)
    {
        _status = Make<OperationResult<BackgroundUpdatesStatusViewModel>>(kind);

        Render<BackgroundUpdates>();

        AssertSnackbars(shown ? [_status] : []);
    }

    /// <summary>After a success, the page reloads the status, which reports nothing.</summary>
    [Theory]
    [InlineData("ToggleEnabled", "success")]
    [InlineData("ToggleEnabled", "failure")]
    [InlineData("ToggleEnabled", "cancelled")]
    [InlineData("TriggerJob", "success")]
    [InlineData("TriggerJob", "failure")]
    [InlineData("TriggerJob", "cancelled")]
    public async Task Action_ReportsEveryResultExceptACancellation(string handler, string kind)
    {
        OperationResult result = handler == "ToggleEnabled"
            ? _enabled = Make<OperationResult<bool>>(kind)
            : _started = Make<OperationResult>(kind);
        var cut = Render<BackgroundUpdates>();
        object?[] args = handler == "TriggerJob" ? ["job_name", "Job"] : [];

        var action = InvokeHandler(cut, handler, args);
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;

        AssertSnackbars(kind == "cancelled" ? [] : [result]);
        Assert.Equal(kind == "success" ? 2 : 1, _updates.CallsTo(nameof(IBackgroundUpdatesService.GetStatusAsync)).Count);
    }
}
