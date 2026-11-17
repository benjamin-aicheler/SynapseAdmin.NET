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
/// How the gallery merges a room's local and remote media, and what each filter chip selects.
/// </summary>
public class RoomGalleryMergeAndFilterTests : DetailPageTestBase
{
    private const string Shared = "mxc://hs/shared";
    private const string Upper = "mxc://hs/CASE";
    private const string Lower = "mxc://hs/case";
    private const string RemoteOnly = "mxc://other.org/remote";

    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.Ordinal)
    {
        [Shared] = "image/png",
        [Upper] = "video/mp4",
        [Lower] = "audio/ogg",
        [RemoteOnly] = "application/pdf"
    };

    public RoomGalleryMergeAndFilterTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(s => s.On(nameof(IRoomService.GetMediaMetadataBatchAsync), args =>
            Task.FromResult(OperationResult<List<RoomMediaItemViewModel>>.Ok(((List<string>)args[0]!)
                .Select(id => new RoomMediaItemViewModel { MediaId = id, MediaType = MediaTypes[id] }).ToList())))));
    }

    /// <summary>
    /// Shared is in both lists, an item without an ID is local, CASE and case differ only in case, and the remote
    /// copy of "case" claims to be local.
    /// </summary>
    private IRenderedComponent<RoomGalleryGrid> RenderGrid()
    {
        var cut = Render<RoomGalleryGrid>(p => p
            .Add(x => x.RoomId, "!room:hs")
            .Add(x => x.Media, new RoomMediaViewModel
            {
                Local = [new() { MediaId = Shared }, new() { MediaId = "" }, new() { MediaId = Upper }],
                Remote = [new() { MediaId = Lower, IsLocal = true }, new() { MediaId = Shared }, new() { MediaId = RemoteOnly }]
            }));
        cut.WaitForAssertion(() => Assert.NotEmpty(Displayed(cut)));
        return cut;
    }

    private static List<string> Displayed(IRenderedComponent<RoomGalleryGrid> cut) =>
        GetField<List<RoomMediaItemViewModel>>(cut.Instance, "_displayedMedia")!.Select(m => m.MediaId).ToList();

    /// <summary>Clicks the chip with this label; "AllMedia" labels both chip sets, so <paramref name="index"/> picks one.</summary>
    private void ClickChip(IRenderedComponent<RoomGalleryGrid> cut, string labelKey, int index = 0)
    {
        var label = Services.GetRequiredService<IStringLocalizer<SharedResources>>()[labelKey].Value;
        cut.FindAll(".mud-chip").Where(c => WebUtility.HtmlDecode(c.TextContent).Trim() == label).ElementAt(index).Click();
    }

    /// <summary>
    /// The first occurrence wins in local-then-remote order, so Shared is local. IDs compare ordinally, items without
    /// an ID are dropped, and each item's source is the list it came from. Each source chip selects its filter; an
    /// empty selection means "all".
    /// </summary>
    [Fact]
    public async Task SourceFilter_ShowsTheMergedMediaOfEachSource()
    {
        var cut = RenderGrid();
        Assert.Equal([Shared, Upper], Displayed(cut));

        ClickChip(cut, "RemoteMedia");
        cut.WaitForAssertion(() => Assert.Equal([Lower, RemoteOnly], Displayed(cut)));

        ClickChip(cut, "AllMedia", index: 0);
        cut.WaitForAssertion(() => Assert.Equal([Shared, Upper, Lower, RemoteOnly], Displayed(cut)));

        ClickChip(cut, "LocalMedia");
        cut.WaitForAssertion(() => Assert.Equal([Shared, Upper], Displayed(cut)));

        await InvokeHandler(cut, "OnSourceFilterChanged", [null]);
        cut.WaitForAssertion(() => Assert.Equal([Shared, Upper, Lower, RemoteOnly], Displayed(cut)));
    }

    /// <summary>Each type chip keeps only its kind of media; an empty selection means "all".</summary>
    [Fact]
    public async Task TypeFilter_ShowsOnlyTheSelectedKindOfMedia()
    {
        var cut = RenderGrid();
        ClickChip(cut, "AllMedia", index: 0);
        cut.WaitForAssertion(() => Assert.Equal(4, Displayed(cut).Count));

        foreach (var (labelKey, expected) in new[] { ("Images", Shared), ("Videos", Upper), ("Audio", Lower), ("Files", RemoteOnly) })
        {
            ClickChip(cut, labelKey);
            cut.WaitForAssertion(() => Assert.Equal([expected], Displayed(cut)));
        }

        ClickChip(cut, "AllMedia", index: 1);
        cut.WaitForAssertion(() => Assert.Equal(4, Displayed(cut).Count));

        ClickChip(cut, "Files");
        cut.WaitForAssertion(() => Assert.Equal([RemoteOnly], Displayed(cut)));
        await InvokeHandler(cut, "OnTypeFilterChanged", [null]);
        cut.WaitForAssertion(() => Assert.Equal(4, Displayed(cut).Count));
    }
}
