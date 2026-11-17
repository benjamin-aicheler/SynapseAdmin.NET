using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Disposing JS interop objects fails once the circuit is gone. The gallery and the time zone service swallow those
/// failures, so their own disposal always completes.
/// </summary>
public class JsDisposalTests : DetailPageTestBase
{
    public JsDisposalTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(s => s.On(nameof(IRoomService.GetMediaMetadataBatchAsync), args =>
            Task.FromResult(OperationResult<List<RoomMediaItemViewModel>>.Ok(((List<string>)args[0]!)
                .Select(id => new RoomMediaItemViewModel { MediaId = id }).ToList())))));
    }

    /// <summary>The observer's "dispose" call fails, then the module's disposal fails; both are attempted.</summary>
    [Fact]
    public async Task RoomGalleryGrid_Disposal_SwallowsFailingJsDisposals()
    {
        var observer = new DisconnectedJsObject([]);
        var module = new DisconnectedJsObject(new() { ["initInfiniteScroll"] = observer });
        // bUnit's own module references can't fail on disposal, so this runtime hands out the failing module.
        Services.AddSingleton<IJSRuntime>(new ModuleJsRuntime(module));
        var cut = Render<RoomGalleryGrid>(p => p
            .Add(x => x.RoomId, "!room:hs")
            .Add(x => x.Media, new RoomMediaViewModel { Local = [new() { MediaId = "mxc://hs/a" }] }));
        cut.WaitForAssertion(() => Assert.Contains("initInfiniteScroll", module.Calls));

        // Called directly: bUnit's own component disposal swallows exceptions, which would hide a rethrow.
        await cut.InvokeAsync(async () => await cut.Instance.DisposeAsync());

        Assert.Equal(["dispose"], observer.Calls);
        Assert.Equal(["initInfiniteScroll", "DisposeAsync"], module.Calls);
    }

    [Fact]
    public async Task TimeZoneService_Disposal_SwallowsAFailingModuleDisposal()
    {
        var module = new DisconnectedJsObject(new() { ["getBrowserTimeZone"] = "UTC" });
        var service = new TimeZoneService(new ModuleJsRuntime(module), NullLogger<TimeZoneService>.Instance);
        await service.InitializeAsync();

        await service.DisposeAsync();

        Assert.Equal(["getBrowserTimeZone", "DisposeAsync"], module.Calls);
    }

    /// <summary>Answers the calls in <paramref name="results"/>; any other call and its disposal fail like a lost circuit.</summary>
    private sealed class DisconnectedJsObject(Dictionary<string, object> results) : IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            return results.TryGetValue(identifier, out var result)
                ? ValueTask.FromResult((TValue)result)
                : ValueTask.FromException<TValue>(new JSDisconnectedException("circuit gone"));
        }

        public ValueTask DisposeAsync()
        {
            Calls.Add(nameof(DisposeAsync));
            return ValueTask.FromException(new JSDisconnectedException("circuit gone"));
        }
    }

    /// <summary>Imports return <paramref name="module"/>; any other call returns a default, like bUnit's loose mode.</summary>
    private sealed class ModuleJsRuntime(IJSObjectReference module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(identifier == "import" ? (TValue)module : default!);
    }
}
