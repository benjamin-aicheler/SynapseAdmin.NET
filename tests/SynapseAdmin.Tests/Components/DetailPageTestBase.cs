using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
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
}
