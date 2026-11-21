using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// The tracked purge of a room lives in the <see cref="IMemoryCache"/> shared by every session. Clearing it for an
/// expected purge removes only that purge, never a newer one another session started (#522).
/// </summary>
public sealed class RoomServiceActivePurgeTests : IDisposable
{
    private const string RoomId = "!room:hs.example";
    private readonly MemoryCache _inner = new(new MemoryCacheOptions());
    private readonly PausingCache _cache;
    private readonly RoomService _rooms;

    public RoomServiceActivePurgeTests()
    {
        _cache = new PausingCache(_inner);
        _rooms = CreateSession();
    }

    /// <summary>One session's <see cref="RoomService"/> (scoped per circuit); all of them share the cache.</summary>
    private RoomService CreateSession()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("get_HomeserverUrl", _ => "https://hs.example"));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new RoomService(session, NullLogger<RoomService>.Instance, new KeyLocalizer(), _cache);
    }

    public void Dispose()
    {
        _cache.Release();
        _inner.Dispose();
    }

    [Fact]
    public void ClearForTheTrackedPurge_RemovesIt()
    {
        _rooms.SetActivePurgeId(RoomId, "purge-1");

        _rooms.ClearActivePurgeId(RoomId, "purge-1");

        Assert.Null(_rooms.GetActivePurgeId(RoomId));
    }

    [Fact]
    public void ClearForAnOlderPurge_KeepsTheNewerOne()
    {
        _rooms.SetActivePurgeId(RoomId, "purge-2");

        _rooms.ClearActivePurgeId(RoomId, "purge-1");

        Assert.Equal("purge-2", _rooms.GetActivePurgeId(RoomId));
    }

    [Fact]
    public void ClearWithoutExpectedPurge_RemovesWhateverIsTracked()
    {
        _rooms.SetActivePurgeId(RoomId, "purge-2");

        _rooms.ClearActivePurgeId(RoomId);

        Assert.Null(_rooms.GetActivePurgeId(RoomId));
    }

    /// <summary>
    /// Session 1 gives up on purge-1 and is paused between reading the tracked purge and removing it, while session 2
    /// starts purge-2. The read and the removal must happen as one step, so purge-2 survives.
    /// </summary>
    [Fact]
    public async Task ClearForAnOlderPurge_RacingWithANewerPurgeFromAnotherSession_KeepsTheNewerOne()
    {
        var otherSession = CreateSession();
        _rooms.SetActivePurgeId(RoomId, "purge-1");
        _cache.PauseNextRead();

        var clear = Task.Run(() => _rooms.ClearActivePurgeId(RoomId, "purge-1"));
        Assert.True(_cache.Paused.Wait(TimeSpan.FromSeconds(10)));
        var set = Task.Run(() => otherSession.SetActivePurgeId(RoomId, "purge-2"));
        // Without coordination the newer purge is written now, while the clear is still paused.
        await Task.WhenAny(set, Task.Delay(200));
        _cache.Release();
        await Task.WhenAll(clear, set).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("purge-2", _rooms.GetActivePurgeId(RoomId));
    }

    /// <summary>An <see cref="IMemoryCache"/> that can pause the next read until the test releases it.</summary>
    private sealed class PausingCache(IMemoryCache inner) : IMemoryCache
    {
        private readonly ManualResetEventSlim _release = new(true);
        private int _pauseNextRead;

        public ManualResetEventSlim Paused { get; } = new(false);

        public void PauseNextRead()
        {
            _release.Reset();
            Volatile.Write(ref _pauseNextRead, 1);
        }

        public void Release() => _release.Set();

        public bool TryGetValue(object key, out object? value)
        {
            var found = inner.TryGetValue(key, out value);
            if (Interlocked.Exchange(ref _pauseNextRead, 0) == 1)
            {
                Paused.Set();
                _release.Wait(TimeSpan.FromSeconds(10));
            }
            return found;
        }

        public ICacheEntry CreateEntry(object key) => inner.CreateEntry(key);

        public void Remove(object key) => inner.Remove(key);

        public void Dispose() { }
    }
}
