using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Issue #384: Synapse's room media list holds one entry per event reference, so the same MXC URI can
/// be listed repeatedly. The service must hand each URI to the UI, and to the homeserver, only once.
/// </summary>
public sealed class RoomMediaDeduplicationTests : IDisposable
{
    private const string RoomId = "!room:example.com";
    private const string RemoteA = "mxc://remote.org/SaAsEGZsRtuLGXzouPfaciXE";
    private const string RemoteB = "mxc://other.org/SaAsEGZsRtuLGXzouPfaciXE"; // same media ID, other server
    private const string RemoteC = "mxc://remote.org/saasegzsrtulgxzoupfacixe"; // differs only in case
    private const string LocalA = "mxc://example.com/local-a";
    private const string LocalB = "mxc://example.com/local-b";

    private sealed class KeyLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private readonly Stub _gateway;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly RoomService _service;
    private SynapseAdminRoomMediaListResult _roomMedia = new();
    private Func<string, CancellationToken, Task<SynapseAdminMediaMetadataResponse.MediaInfo?>> _metadata =
        (mxc, _) => Task.FromResult<SynapseAdminMediaMetadataResponse.MediaInfo?>(new() { UploadName = "name:" + mxc });

    public RoomMediaDeduplicationTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(out _gateway);
        _gateway
            .On("get_HomeserverUrl", _ => "https://example.com")
            .On(nameof(IMatrixGateway.GetRoomDetailsAsync), _ =>
                Task.FromResult<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?>(new() { RoomId = RoomId }))
            .On(nameof(IMatrixGateway.GetRoomMediaListAsync), _ =>
                Task.FromResult<SynapseAdminRoomMediaListResult?>(_roomMedia))
            .On(nameof(IMatrixGateway.GetMediaMetadataAsync), args =>
                _metadata((string)args[0]!, (CancellationToken)args[^1]!));

        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        _service = new RoomService(session, NullLogger<RoomService>.Instance, new KeyLocalizer(), _cache);
    }

    public void Dispose() => _cache.Dispose();

    private List<string> MetadataRequests =>
        [.. _gateway.CallsTo(nameof(IMatrixGateway.GetMediaMetadataAsync)).Select(a => (string)a[0]!)];

    [Fact]
    public async Task RoomDetails_ListsRepeatedMxcsOnce_PreservingOrderAndOrigin()
    {
        _roomMedia = new SynapseAdminRoomMediaListResult
        {
            Local = [LocalA, LocalB, LocalA, LocalB, LocalA],
            Remote = [RemoteA, RemoteB, RemoteA, RemoteC, RemoteA]
        };

        var result = await _service.GetRoomDetailsAsync(RoomId);

        Assert.True(result.Success);
        var media = result.Data!.Media!;
        Assert.Equal([LocalA, LocalB], media.Local.Select(m => m.MediaId));
        Assert.All(media.Local, m => Assert.True(m.IsLocal));
        // Same bare media ID on another server, and a case variant, are distinct media.
        Assert.Equal([RemoteA, RemoteB, RemoteC], media.Remote.Select(m => m.MediaId));
        Assert.All(media.Remote, m => Assert.False(m.IsLocal));
    }

    [Fact]
    public async Task RoomDetails_IgnoresEmptyEntries()
    {
        _roomMedia = new SynapseAdminRoomMediaListResult { Local = [LocalA, "", LocalA], Remote = [null!, RemoteA] };

        var result = await _service.GetRoomDetailsAsync(RoomId);

        var media = result.Data!.Media!;
        Assert.Equal([LocalA], media.Local.Select(m => m.MediaId));
        Assert.Equal([RemoteA], media.Remote.Select(m => m.MediaId));
    }

    [Fact]
    public async Task MetadataBatch_RequestsEachMxcOnce_AndReturnsOneResultPerMxc()
    {
        var result = await _service.GetMediaMetadataBatchAsync([RemoteA, RemoteB, RemoteA, RemoteC, RemoteA]);

        Assert.True(result.Success);
        var items = result.Data!;
        Assert.Equal([RemoteA, RemoteB, RemoteC], items.Select(m => m.MediaId));
        Assert.Equal("name:" + RemoteB, items[1].UploadName);
        // Requests run concurrently, so compare them as a set.
        Assert.Equal(
            new[] { RemoteA, RemoteB, RemoteC }.Order(StringComparer.Ordinal),
            MetadataRequests.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MetadataBatch_MissingMetadata_StillReturnsOneUnhydratedResultPerMxc()
    {
        _metadata = (_, _) => Task.FromResult<SynapseAdminMediaMetadataResponse.MediaInfo?>(null);

        var result = await _service.GetMediaMetadataBatchAsync([RemoteA, RemoteA]);

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!);
        Assert.Equal(RemoteA, item.MediaId);
        Assert.Null(item.UploadName);
        Assert.Single(MetadataRequests);
    }

    [Fact]
    public async Task MetadataBatch_Cancelled_ReturnsSilentCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _service.GetMediaMetadataBatchAsync([RemoteA, RemoteA, RemoteB], cts.Token);

        Assert.False(result.Success);
        Assert.Equal(Severity.Normal, result.Severity);
        Assert.Empty(MetadataRequests);
    }
}
