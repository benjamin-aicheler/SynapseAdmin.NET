using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// #397 (D-09): the gallery's "file" type filter (anything that is not image, video or audio) had no chip in the
/// UI, so it could not be selected.
/// </summary>
public class RoomGalleryFilterTests : DetailPageTestBase
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.Ordinal)
    {
        ["mxc://hs/photo"] = "image/png",
        ["mxc://hs/clip"] = "video/mp4",
        ["mxc://hs/song"] = "audio/ogg",
        ["mxc://hs/report"] = "application/pdf",
        ["mxc://hs/archive"] = "application/zip"
    };

    public RoomGalleryFilterTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(s => s.On(nameof(IRoomService.GetMediaMetadataBatchAsync), args =>
            Task.FromResult(OperationResult<List<RoomMediaItemViewModel>>.Ok(((List<string>)args[0]!)
                .Select(id => new RoomMediaItemViewModel { MediaId = id, MediaType = MediaTypes[id] }).ToList())))));
    }

    private IRenderedComponent<RoomGalleryGrid> RenderGrid() =>
        Render<RoomGalleryGrid>(p => p
            .Add(x => x.RoomId, "!room:hs")
            .Add(x => x.Media, new RoomMediaViewModel
            {
                Local = MediaTypes.Keys.Select(id => new RoomMediaItemViewModel { MediaId = id, IsLocal = true }).ToList(),
                Remote = []
            }));

    [Fact]
    public void TypeFilter_ShowsFilesChip()
    {
        var files = Services.GetRequiredService<IStringLocalizer<SharedResources>>()["Files"].Value;

        var cut = RenderGrid();

        cut.WaitForAssertion(() => Assert.Contains(
            cut.FindAll(".mud-chip"), chip => WebUtility.HtmlDecode(chip.TextContent).Trim() == files));
    }

    [Fact]
    public async Task FilesFilter_ShowsOnlyNonImageVideoAudioMedia()
    {
        var cut = RenderGrid();
        cut.WaitForAssertion(() => Assert.Equal(5, GetField<List<RoomMediaItemViewModel>>(cut.Instance, "_displayedMedia")!.Count));

        await InvokeHandler(cut, "OnTypeFilterChanged", "file");

        cut.WaitForAssertion(() => Assert.Equal(
            ["mxc://hs/report", "mxc://hs/archive"],
            GetField<List<RoomMediaItemViewModel>>(cut.Instance, "_displayedMedia")!.Select(m => m.MediaId)));
    }
}
