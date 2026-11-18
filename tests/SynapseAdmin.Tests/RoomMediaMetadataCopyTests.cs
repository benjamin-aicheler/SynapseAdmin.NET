using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// The metadata fields that the room service copies from the homeserver onto a room media item.
/// </summary>
public sealed class RoomMediaMetadataCopyTests : IDisposable
{
    private const string Mxc = "mxc://remote.org/abc";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    private static SynapseAdminMediaMetadataResponse.MediaInfo Metadata(bool? safeFromQuarantine) => new()
    {
        MediaOrigin = "remote.org",
        UserId = "@u:remote.org",
        MediaId = "abc",
        UploadName = "photo.png",
        MediaType = "image/png",
        MediaLength = 1234,
        CreatedTimestamp = 1_700_000_000_000,
        QuarantinedBy = "@admin:example.com",
        SafeFromQuarantine = safeFromQuarantine
    };

    /// <summary>
    /// The service copies the 6 metadata fields, keeps the MXC URI as the ID (not the bare media ID) and reads a
    /// missing <c>safe_from_quarantine</c> as false.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task MetadataBatch_CopiesTheMetadataFields(bool? safeFromQuarantine, bool expectedSafe)
    {
        var gateway = Stub.Create<IMatrixGateway>(g => g.On(nameof(IMatrixGateway.GetMediaMetadataAsync), _ =>
            Task.FromResult<SynapseAdminMediaMetadataResponse.MediaInfo?>(Metadata(safeFromQuarantine))));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var service = new RoomService(session, NullLogger<RoomService>.Instance, new KeyLocalizer(), _cache);

        var result = await service.GetMediaMetadataBatchAsync([Mxc]);

        Assert.True(result.Success);
        Assert.Equivalent(new RoomMediaItemViewModel
        {
            MediaId = Mxc,
            UploadName = "photo.png",
            MediaType = "image/png",
            MediaLength = 1234,
            CreatedTimestamp = 1_700_000_000_000,
            QuarantinedBy = "@admin:example.com",
            SafeFromQuarantine = expectedSafe
        }, Assert.Single(result.Data!), strict: true);
    }
}
