using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// How the gallery copies the metadata it loads onto the room's media items.
/// </summary>
public class RoomGalleryMetadataHydrationTests : DetailPageTestBase
{
    private const string Mxc = "mxc://hs/local";

    /// <summary>
    /// The loaded metadata replaces all 6 metadata fields, also with empty values, while the item keeps its ID and
    /// stays local although the loaded copy says remote.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadedMetadata_ReplacesTheMetadataFields(bool filled)
    {
        var meta = filled
            ? new RoomMediaItemViewModel
            {
                MediaId = Mxc,
                UploadName = "new.png",
                MediaType = "image/png",
                MediaLength = 42,
                CreatedTimestamp = 1_700_000_000_000,
                QuarantinedBy = "@admin:hs",
                SafeFromQuarantine = true
            }
            : new RoomMediaItemViewModel { MediaId = Mxc };
        Services.AddSingleton(Stub.Create<IRoomService>(s => s.On(nameof(IRoomService.GetMediaMetadataBatchAsync), _ =>
            Task.FromResult(OperationResult<List<RoomMediaItemViewModel>>.Ok([meta])))));
        var item = new RoomMediaItemViewModel
        {
            MediaId = Mxc,
            UploadName = "old.txt",
            MediaType = "text/plain",
            MediaLength = 7,
            CreatedTimestamp = 1,
            QuarantinedBy = "@old:hs",
            SafeFromQuarantine = !filled
        };

        var cut = Render<RoomGalleryGrid>(p => p
            .Add(x => x.RoomId, "!room:hs")
            .Add(x => x.Media, new RoomMediaViewModel { Local = [item] }));

        cut.WaitForAssertion(() => Assert.Equivalent(new RoomMediaItemViewModel
        {
            MediaId = Mxc,
            IsLocal = true,
            UploadName = meta.UploadName,
            MediaType = meta.MediaType,
            MediaLength = meta.MediaLength,
            CreatedTimestamp = meta.CreatedTimestamp,
            QuarantinedBy = meta.QuarantinedBy,
            SafeFromQuarantine = meta.SafeFromQuarantine
        }, Assert.Single(GetField<List<RoomMediaItemViewModel>>(cut.Instance, "_displayedMedia")!), strict: true));
    }
}
