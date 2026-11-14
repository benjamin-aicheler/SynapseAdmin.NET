using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Issue #384: a room that references the same MXC URI more than once crashed the gallery's
/// Remote and All filters ("An item with the same key has already been added").
/// </summary>
public class RoomGalleryDuplicateMediaTests : DetailPageTestBase
{
    private const string RoomId = "!room:hs";
    private const string Local = "mxc://hs/local";
    private const string Remote = "mxc://unredacted.org/SaAsEGZsRtuLGXzouPfaciXE";
    private const string RemoteOtherServer = "mxc://other.org/SaAsEGZsRtuLGXzouPfaciXE";

    private readonly Stub _rooms;

    /// <summary>When set, the metadata stub echoes every requested ID twice.</summary>
    private bool _repeatMetadataResults;

    /// <summary>Media size the metadata stub reports for every item.</summary>
    private long _metadataLength;

    public RoomGalleryDuplicateMediaTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        // Like the pre-fix service: one result per requested occurrence, with or without metadata.
        _rooms.On(nameof(IRoomService.GetMediaMetadataBatchAsync), args =>
        {
            var ids = (List<string>)args[0]!;
            if (_repeatMetadataResults) ids = [.. ids, .. ids];
            return Task.FromResult(OperationResult<List<RoomMediaItemViewModel>>.Ok(
                ids.Select(id => new RoomMediaItemViewModel { MediaId = id, UploadName = "file-" + id, MediaLength = _metadataLength }).ToList()));
        });
    }

    private static RoomMediaViewModel MediaWith(IEnumerable<string> local, IEnumerable<string> remote) => new()
    {
        Local = local.Select(id => new RoomMediaItemViewModel { MediaId = id, IsLocal = true }).ToList(),
        Remote = remote.Select(id => new RoomMediaItemViewModel { MediaId = id }).ToList()
    };

    private IRenderedComponent<RoomGalleryGrid> RenderGrid(RoomMediaViewModel media) =>
        Render<RoomGalleryGrid>(p => p.Add(x => x.RoomId, RoomId).Add(x => x.Media, media));

    private static List<string> Displayed(IRenderedComponent<RoomGalleryGrid> cut) =>
        GetField<List<RoomMediaItemViewModel>>(cut.Instance, "_displayedMedia")!.Select(m => m.MediaId).ToList();

    private List<string> RequestedMetadataIds =>
        _rooms.CallsTo(nameof(IRoomService.GetMediaMetadataBatchAsync)).SelectMany(a => (List<string>)a[0]!).ToList();

    [Theory]
    [InlineData("remote")]
    [InlineData("all")]
    public async Task RepeatedRemoteMxc_SourceFilter_ShowsEachMediaOnce(string filter)
    {
        var cut = RenderGrid(MediaWith([Local], [Remote, Remote, RemoteOtherServer, Remote]));

        await InvokeHandler(cut, "OnSourceFilterChanged", filter);

        var expected = filter == "all" ? new[] { Local, Remote, RemoteOtherServer } : [Remote, RemoteOtherServer];
        cut.WaitForAssertion(() => Assert.Equal(expected, Displayed(cut)));
        Assert.Equal(RequestedMetadataIds.Distinct(StringComparer.Ordinal), RequestedMetadataIds);
        Assert.All(GetField<List<RoomMediaItemViewModel>>(cut.Instance, "_displayedMedia")!,
            m => Assert.Equal("file-" + m.MediaId, m.UploadName));
    }

    /// <summary>#395 (H-04): gallery cards use the shared size formatter, not KB/MB-only output.</summary>
    [Theory]
    [InlineData(500L)]
    [InlineData(2_097_152L)]
    [InlineData(5_368_709_120L)]
    public async Task MediaSize_UsesSharedFormatter(long bytes)
    {
        _metadataLength = bytes;
        var cut = RenderGrid(MediaWith([Local], []));

        await InvokeHandler(cut, "OnSourceFilterChanged", "all");

        cut.WaitForAssertion(() => Assert.Contains(bytes.FormatSize(), System.Net.WebUtility.HtmlDecode(cut.Markup)));
    }

    [Fact]
    public async Task RepeatedMetadataResults_DoNotThrow()
    {
        _repeatMetadataResults = true;
        var cut = RenderGrid(MediaWith([Local], [Remote, RemoteOtherServer]));

        await InvokeHandler(cut, "OnSourceFilterChanged", "all");

        cut.WaitForAssertion(() => Assert.Equal([Local, Remote, RemoteOtherServer], Displayed(cut)));
    }

    [Fact]
    public async Task RepeatsAcrossBatchBoundary_AreShownAndRequestedOnce()
    {
        // 25 unique remote items (more than one 20-item batch), each listed twice: once in the first
        // half and again in the second, so repeats straddle the batch boundary.
        var unique = Enumerable.Range(0, 25).Select(i => $"mxc://remote.org/m{i:D2}").ToList();
        var cut = RenderGrid(MediaWith([], [.. unique, .. unique]));

        await InvokeHandler(cut, "OnSourceFilterChanged", "remote");
        cut.WaitForAssertion(() => Assert.Equal(20, Displayed(cut).Count));

        await cut.InvokeAsync(() => cut.Instance.OnSentinelIntersected());

        cut.WaitForAssertion(() => Assert.Equal(unique, Displayed(cut)));
        Assert.Equal(unique, RequestedMetadataIds);
    }

    [Fact]
    public async Task SwitchingFiltersAfterHydration_DoesNotRequestMetadataAgain()
    {
        var cut = RenderGrid(MediaWith([Local, Local], [Remote, Remote]));
        cut.WaitForAssertion(() => Assert.Equal([Local], Displayed(cut)));

        foreach (var filter in new[] { "remote", "all", "remote", "local", "all" })
        {
            await InvokeHandler(cut, "OnSourceFilterChanged", filter);
        }

        cut.WaitForAssertion(() => Assert.Equal([Local, Remote], Displayed(cut)));
        Assert.Equal([Local, Remote], RequestedMetadataIds);
    }

    [Fact]
    public async Task RoomGalleryPage_RemoteFilterWithRepeatedMxc_DoesNotCrash()
    {
        _rooms.On(nameof(IRoomService.GetRoomDetailsAsync), _ =>
            Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel
            {
                RoomId = RoomId,
                Name = "Gallery Room",
                Media = MediaWith([Local], [Remote, Remote])
            })));

        var page = Render<RoomGallery>(p => p.Add(x => x.RoomId, RoomId));
        var grid = page.WaitForComponent<RoomGalleryGrid>();

        await InvokeHandler(grid, "OnSourceFilterChanged", "remote");

        grid.WaitForAssertion(() => Assert.Equal([Remote], Displayed(grid)));
    }
}
