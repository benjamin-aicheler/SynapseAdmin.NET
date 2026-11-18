using MudBlazor;
using SynapseAdmin.Models;

namespace SynapseAdmin.Extensions;

public static class SnackbarExtensions
{
    /// <summary>
    /// Shows the message of <paramref name="result"/> with its severity, after a success or a failure, unless the
    /// result is a silent cancellation (<see cref="Severity.Normal"/>). Returns whether a snackbar was shown.
    /// </summary>
    public static bool ShowResult(this ISnackbar snackbar, OperationResult result)
    {
        if (result.Severity != Severity.Normal)
        {
            snackbar.Add(result.Message, result.Severity);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Shows the message of <paramref name="result"/> with its severity only after a failure that is not a silent
    /// cancellation. Returns whether a snackbar was shown.
    /// </summary>
    public static bool ShowFailure(this ISnackbar snackbar, OperationResult result) =>
        !result.Success && snackbar.ShowResult(result);
}
