using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Models;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// <see cref="SnackbarExtensions.ShowResult"/> shows successes and failures, <see cref="SnackbarExtensions.ShowFailure"/>
/// only failures, and neither shows a silent cancellation. Both report whether they showed a snackbar.
/// </summary>
public class SnackbarExtensionsTests
{
    [Theory]
    [InlineData(nameof(SnackbarExtensions.ShowResult), "success", true)]
    [InlineData(nameof(SnackbarExtensions.ShowResult), "failure", true)]
    [InlineData(nameof(SnackbarExtensions.ShowResult), "cancelled", false)]
    [InlineData(nameof(SnackbarExtensions.ShowFailure), "success", false)]
    [InlineData(nameof(SnackbarExtensions.ShowFailure), "failure", true)]
    [InlineData(nameof(SnackbarExtensions.ShowFailure), "cancelled", false)]
    public void ShowsTheMessageWithItsSeverity(string helper, string kind, bool shown)
    {
        var result = kind switch
        {
            "success" => OperationResult.Ok("Done"),
            "failure" => OperationResult.Failure("Not found", Severity.Warning),
            _ => OperationResult.Cancelled()
        };
        var snackbar = Stub.Create<ISnackbar>(out var stub);

        var returned = helper == nameof(SnackbarExtensions.ShowResult) ? snackbar.ShowResult(result) : snackbar.ShowFailure(result);

        Assert.Equal(shown, returned);
        var calls = stub.CallsTo(nameof(ISnackbar.Add));
        if (shown)
        {
            Assert.Equal(new object?[] { result.Message, result.Severity, null, null }, Assert.Single(calls));
        }
        else
        {
            Assert.Empty(calls);
        }
    }
}
