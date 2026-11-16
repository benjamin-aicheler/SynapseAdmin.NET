using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the homeserver-scoped <see cref="RoomService"/> keys in the shared <see cref="IMemoryCache"/>:
/// the homeserver URL loses its trailing slash and is lower-cased, while the room ID is kept as is.
/// </summary>
public sealed class RoomServiceCacheKeyTests : IDisposable
{
    private const string RoomId = "!Room:HS.example";
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly RoomService _rooms;

    public RoomServiceCacheKeyTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_HomeserverUrl", _ => "https://HS.example/")
            .On("GetLargestRoomsAsync", _ => Task.FromResult<RoomStatisticsResponse?>(new() { Rooms = [new() { RoomId = RoomId }] }))
            .On("GetRoomDetailsAsync", _ =>
                Task.FromResult<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?>(new() { RoomId = RoomId, Name = "Lobby" })));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var localizer = Stub.Create<IStringLocalizer<SharedResources>>(s =>
            s.On("get_Item", args => new LocalizedString((string)args[0]!, (string)args[0]!)));
        _rooms = new RoomService(session, NullLogger<RoomService>.Instance, localizer, _cache);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task RoomNameKey_IsScopedToTheNormalizedHomeserverUrl()
    {
        var result = await _rooms.GetLargestRoomsAsync();

        Assert.True(result.Success);
        Assert.True(_cache.TryGetValue("room_name:https://hs.example:!Room:HS.example", out string? name));
        Assert.Equal("Lobby", name);
    }

    [Fact]
    public void ActivePurgeKey_IsScopedToTheNormalizedHomeserverUrl()
    {
        _rooms.SetActivePurgeId(RoomId, "purge-1");

        Assert.True(_cache.TryGetValue("active_purge:https://hs.example:!Room:HS.example", out string? purgeId));
        Assert.Equal("purge-1", purgeId);
    }
}
