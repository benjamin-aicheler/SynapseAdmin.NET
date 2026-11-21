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
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly RoomService _rooms;

    public RoomServiceActivePurgeTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("get_HomeserverUrl", _ => "https://hs.example"));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        _rooms = new RoomService(session, NullLogger<RoomService>.Instance, new KeyLocalizer(), _cache);
    }

    public void Dispose() => _cache.Dispose();

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
}
