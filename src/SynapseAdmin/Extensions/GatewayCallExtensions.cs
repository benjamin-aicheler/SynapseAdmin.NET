using MudBlazor;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Extensions;

/// <summary>
/// The shared error policy of the service layer for one gateway call: without a session the call fails with
/// <c>NotAuthenticated</c> and <c>call</c> never runs; a cancellation returns a silent
/// <see cref="OperationResult.Cancelled"/>; a gateway's <see cref="OperationNotSupportedByHomeserverException"/> goes to
/// <c>logFailure</c> and returns the localized <c>OperationNotSupportedByHomeserver</c> warning (ADR 0006); any other
/// exception, including a plain <see cref="NotSupportedException"/>, goes to <c>logFailure</c> and returns the localized
/// <c>failureKey</c>. The session's gateway is read once and passed to <c>call</c>. Validation that has to run after
/// the session check belongs at the start of <c>call</c>.
/// </summary>
internal static class GatewayCallExtensions
{
    private const string NotSupportedKey = "OperationNotSupportedByHomeserver";

    public static async Task<OperationResult> RunGatewayCallAsync(this IMatrixSessionService session,
        IStringLocalizer<SharedResources> localizer, string failureKey, Action<Exception> logFailure,
        Func<IMatrixGateway, Task<OperationResult>> call)
    {
        var gateway = session.Gateway;
        if (gateway == null) return OperationResult.Failure(localizer["NotAuthenticated"]);

        try
        {
            return await call(gateway);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (OperationNotSupportedByHomeserverException ex)
        {
            logFailure(ex);
            return OperationResult.Failure(localizer[NotSupportedKey], Severity.Warning);
        }
        catch (Exception ex)
        {
            logFailure(ex);
            return OperationResult.Failure(localizer[failureKey]);
        }
    }

    public static async Task<OperationResult<T>> RunGatewayCallAsync<T>(this IMatrixSessionService session,
        IStringLocalizer<SharedResources> localizer, string failureKey, Action<Exception> logFailure,
        Func<IMatrixGateway, Task<OperationResult<T>>> call)
    {
        var gateway = session.Gateway;
        if (gateway == null) return OperationResult<T>.Failure(localizer["NotAuthenticated"]);

        try
        {
            return await call(gateway);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<T>.Cancelled();
        }
        catch (OperationNotSupportedByHomeserverException ex)
        {
            logFailure(ex);
            return OperationResult<T>.Failure(localizer[NotSupportedKey], Severity.Warning);
        }
        catch (Exception ex)
        {
            logFailure(ex);
            return OperationResult<T>.Failure(localizer[failureKey]);
        }
    }
}
