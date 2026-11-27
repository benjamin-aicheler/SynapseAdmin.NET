using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Shared bUnit setup for the room/user detail pages: real MudBlazor + localization,
/// recording stubs for the session, dialogs and snackbar.
/// </summary>
public abstract class DetailPageTestBase : BunitContext, IAsyncLifetime
{
    // MudBlazor services are IAsyncDisposable only; xUnit 2 calls DisposeAsync before Dispose,
    // and the synchronous Dispose is a no-op once the context has been disposed asynchronously.
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    protected Stub DialogStub { get; }
    protected Stub SnackbarStub { get; }

    /// <summary>Pending confirmations, in the order they were shown: (message, completion).</summary>
    protected List<(string Message, TaskCompletionSource<bool?> Result)> Confirmations { get; } = [];

    protected DetailPageTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLocalization();
        Services.AddLogging();

        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_SupportsAdminApi", _ => true)
            .On("get_ServerName", _ => "hs"));
        Services.AddSingleton(Stub.Create<IMatrixSessionService>(s => s
            .On("get_Gateway", _ => gateway)
            .On("get_IsLoggedIn", _ => true)));
        Services.AddSingleton(Stub.Create<ITimeZoneService>(s => s
            .On("get_UserTimeZone", _ => TimeZoneInfo.Utc)
            .On("ToLocal", a => a[0])
            .On("GetLocalNow", _ => DateTimeOffset.UtcNow)
            .On("GetDisplayOffset", _ => "UTC")));
        Services.AddSingleton(Stub.Create<IMediaService>());
        // The Devices and Recent connections tabs (#598) load only when opened; empty lists if a page test opens them.
        Services.AddSingleton(Stub.Create<IUserDeviceService>(s => s
            .On(nameof(IUserDeviceService.GetUserDevicesAsync), _ => Task.FromResult(OperationResult<List<UserDeviceViewModel>>.Ok([])))
            .On(nameof(IUserDeviceService.GetUserConnectionsAsync), _ => Task.FromResult(OperationResult<List<UserConnectionViewModel>>.Ok([])))));

        // No redaction is tracked unless a page test says so (#600).
        Services.AddSingleton(Stub.Create<IUserRedactionService>());

        Services.AddSingleton(Stub.Create<IDialogService>(out var dialogStub));
        DialogStub = dialogStub;
        DialogStub.On(nameof(IDialogService.ShowMessageBoxAsync), args =>
        {
            var tcs = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Confirmations) Confirmations.Add((args[1]?.ToString() ?? string.Empty, tcs));
            return tcs.Task;
        });

        Services.AddSingleton(Stub.Create<ISnackbar>(out var snackbarStub));
        SnackbarStub = snackbarStub;
    }

    protected static TaskCompletionSource<T> Pending<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Invokes a private event handler on the component's dispatcher without awaiting it.</summary>
    protected static Task InvokeHandler<TComponent>(IRenderedComponent<TComponent> cut, string name, params object?[] args)
        where TComponent : IComponent
    {
        var method = typeof(TComponent).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? throw new MissingMethodException(typeof(TComponent).Name, name);
        return cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, args)!);
    }

    protected static T? GetField<T>(object instance, string name) =>
        (T?)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance);

    protected bool WarningShown() =>
        SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Any(a => a.Length > 1 && a[1] is Severity.Warning);

    /// <summary>Asserts the only dialog shown: the media preview, with its title, parameters and options.</summary>
    protected void AssertPreviewShown(string title, string titleParameter, string previewUrl, string? mediaType)
    {
        var args = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
        Assert.Equal(title, args[0]);
        var parameters = (DialogParameters)args[1]!;
        Assert.Equal(3, parameters.Count);
        Assert.Equal((titleParameter, previewUrl, mediaType), ((string?)parameters["Title"], (string?)parameters["PreviewUrl"], (string?)parameters["MediaType"]));
        Assert.Equivalent(new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true }, args[2], strict: true);
    }

    /// <summary>Makes every dialog shown through <see cref="IDialogService.ShowAsync"/> close as cancelled at once.</summary>
    protected void CancelDialogs() =>
        DialogStub.On(nameof(IDialogService.ShowAsync), _ => Task.FromResult(
            Stub.Create<IDialogReference>(s => s.On("get_Result", _ => Task.FromResult<DialogResult?>(DialogResult.Cancel())))));

    /// <summary>Asserts the options of every dialog shown through <see cref="IDialogService.ShowAsync"/>, in order.</summary>
    protected void AssertDialogOptions(params DialogOptions[] expected)
    {
        var shown = DialogStub.CallsTo(nameof(IDialogService.ShowAsync)).Select(args => args[^1]).ToList();
        Assert.Equal(expected.Length, shown.Count);
        foreach (var (options, actual) in expected.Zip(shown)) Assert.Equivalent(options, actual, strict: true);
    }

    /// <summary>"failure" fails, "cancelled" is a silent cancellation, "success" succeeds without data.</summary>
    protected static TResult Make<TResult>(string kind) where TResult : OperationResult, new() => kind switch
    {
        "failure" => new TResult { Success = false, Message = "Failed", Severity = Severity.Error },
        "cancelled" => new TResult { Success = false, Message = "Operation cancelled", Severity = Severity.Normal },
        _ => new TResult { Success = true, Message = "Done", Severity = Severity.Success }
    };

    /// <summary>
    /// <paramref name="result"/>'s outcome as an <see cref="OperationResult{T}"/>, for a stub method that returns data while
    /// the test shares one result between several actions.
    /// </summary>
    protected static OperationResult<T> WithData<T>(OperationResult result, T? data = default) =>
        new() { Success = result.Success, Message = result.Message, Severity = result.Severity, Data = data };

    /// <summary>Asserts the snackbars shown so far: exactly these results' messages and severities, in order.</summary>
    protected void AssertSnackbars(params OperationResult[] shown) =>
        Assert.Equal(
            shown.Select(r => ((string?)r.Message, r.Severity)),
            SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Select(a => ((string?)a[0], (Severity)a[1]!)));
}
