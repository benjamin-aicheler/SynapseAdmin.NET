using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// #395 (H-12): a null gateway result means an empty collection for collection reads, and a failure for
/// single-resource reads and operations. The unconfigured <see cref="Stub"/> gateway returns null for every call.
/// </summary>
public sealed class NullResultContractTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly RoomService _rooms;

    public NullResultContractTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("get_HomeserverUrl", _ => "https://example.com"));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        _rooms = new RoomService(session, NullLogger<RoomService>.Instance, new KeyLocalizer(), _cache);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task PublicRooms_NullResult_IsEmptyDirectory()
    {
        var result = await _rooms.GetPublicRoomsAsync(20);

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Chunk);
    }

    [Fact]
    public async Task LocalAliases_NullResult_IsEmptyList()
    {
        var result = await _rooms.GetLocalRoomAliasesAsync("!room:example.com");

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task RoomList_NullResult_IsEmptyList()
    {
        var result = await _rooms.GetRoomListAsync(0, 10, "name", MudBlazor.SortDirection.Ascending);

        Assert.True(result.Success);
        Assert.Empty(result.Data.Rooms);
    }

    [Fact]
    public async Task RoomDetails_NullResult_IsFailure()
    {
        var result = await _rooms.GetRoomDetailsAsync("!room:example.com");

        Assert.False(result.Success);
        Assert.Equal("RoomNotFound", result.Message);
    }

    [Fact]
    public async Task PurgeHistory_NullResult_IsFailure()
    {
        var result = await _rooms.PurgeRoomHistoryAsync("!room:example.com", new SynapseAdmin.Models.ViewModels.PurgeHistoryViewModel());

        Assert.False(result.Success);
    }

    private sealed class KeyLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
