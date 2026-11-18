using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Extensions.Mapping;
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
/// The metadata fields that the room service copies from the homeserver onto a room media item, and the two shared
/// copy helpers.
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

    private static RoomMediaItemViewModel OldItem(bool safeFromQuarantine) => new()
    {
        MediaId = Mxc,
        IsLocal = true,
        UploadName = "old.txt",
        MediaType = "text/plain",
        MediaLength = 7,
        CreatedTimestamp = 1,
        QuarantinedBy = "@old:hs",
        SafeFromQuarantine = safeFromQuarantine
    };

    /// <summary>
    /// <see cref="RoomMediaItemViewModel.CopyMetadataFrom"/> replaces the 6 metadata fields, also with empty values,
    /// and keeps the item's ID and source although the copy has others.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CopyMetadataFrom_ReplacesOnlyTheMetadataFields(bool filled)
    {
        var other = filled
            ? new RoomMediaItemViewModel
            {
                MediaId = "mxc://other.org/x",
                UploadName = "new.png",
                MediaType = "image/png",
                MediaLength = 42,
                CreatedTimestamp = 5,
                QuarantinedBy = "@admin:hs",
                SafeFromQuarantine = true
            }
            : new RoomMediaItemViewModel { MediaId = "mxc://other.org/x" };
        var item = OldItem(safeFromQuarantine: !filled);

        item.CopyMetadataFrom(other);

        Assert.Equivalent(new RoomMediaItemViewModel
        {
            MediaId = Mxc,
            IsLocal = true,
            UploadName = other.UploadName,
            MediaType = other.MediaType,
            MediaLength = other.MediaLength,
            CreatedTimestamp = other.CreatedTimestamp,
            QuarantinedBy = other.QuarantinedBy,
            SafeFromQuarantine = other.SafeFromQuarantine
        }, item, strict: true);
    }

    /// <summary>
    /// <see cref="RoomMappingExtensions.ApplyMetadata"/> also overwrites with empty metadata (whose media type is an
    /// empty string), reads a missing safe flag as false, and keeps the ID and source.
    /// </summary>
    [Fact]
    public void ApplyMetadata_OverwritesWithEmptyMetadata()
    {
        var item = OldItem(safeFromQuarantine: true);

        item.ApplyMetadata(new SynapseAdminMediaMetadataResponse.MediaInfo { MediaId = "abc" });

        Assert.Equivalent(new RoomMediaItemViewModel { MediaId = Mxc, IsLocal = true, MediaType = "" }, item, strict: true);
    }
}
