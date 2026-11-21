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
/// Issue #523: room media arrives as bare <c>mxc://</c> URIs, so the gallery's search and type filters need metadata
/// that only lookups provide. Without such a filter, the gallery looks up only what it displays. With one, it checks
/// the room's items in order, in bounded batches, and says when its answer is not complete yet (AC-10).
/// </summary>
public class RoomGalleryFilterHydrationTests : DetailPageTestBase
{
    private const string Needle = "needle.png";

    /// <summary>Every metadata lookup batch, in call order.</summary>
    private readonly List<List<string>> _batches = [];

    /// <summary>When set, the next lookup batch waits for this result instead of answering at once.</summary>
    private TaskCompletionSource<OperationResult<List<RoomMediaItemViewModel>>>? _heldBatch;

    /// <summary>Metadata per MXC URI. A missing entry is a lookup that finds nothing (for example uncached remote media).</summary>
    private readonly Dictionary<string, (string Type, string Name)> _metadata = new(StringComparer.Ordinal);

    public RoomGalleryFilterHydrationTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(s => s.On(nameof(IRoomService.GetMediaMetadataBatchAsync), args =>
        {
            var ids = (List<string>)args[0]!;
            _batches.Add([.. ids]);
            if (_heldBatch is { } held)
            {
                _heldBatch = null;
                return held.Task;
            }
            return Task.FromResult(Answer(ids));
        })));
    }

    private OperationResult<List<RoomMediaItemViewModel>> Answer(IEnumerable<string> ids) =>
        OperationResult<List<RoomMediaItemViewModel>>.Ok(ids.Select(id => _metadata.TryGetValue(id, out var m)
            ? new RoomMediaItemViewModel { MediaId = id, MediaType = m.Type, UploadName = m.Name }
            : new RoomMediaItemViewModel { MediaId = id }).ToList());

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static string Mxc(int i) => $"mxc://hs/m{i:D2}";

    /// <summary>
    /// <paramref name="count"/> local text files; item <paramref name="needleAt"/> (1-based), if any, is the only image
    /// and the only item named <see cref="Needle"/>.
    /// </summary>
    private IRenderedComponent<RoomGalleryGrid> RenderGrid(int count, int? needleAt = null, List<string>? remote = null)
    {
        for (var i = 1; i <= count; i++)
        {
            _metadata[Mxc(i)] = i == needleAt ? ("image/png", Needle) : ("text/plain", $"doc{i:D2}.txt");
        }
        var cut = Render<RoomGalleryGrid>(p => p
            .Add(x => x.RoomId, "!room:hs")
            .Add(x => x.Media, new RoomMediaViewModel
            {
                Local = Enumerable.Range(1, count).Select(i => new RoomMediaItemViewModel { MediaId = Mxc(i) }).ToList(),
                Remote = (remote ?? []).Select(id => new RoomMediaItemViewModel { MediaId = id }).ToList()
            }));
        cut.WaitForAssertion(() => Assert.NotEmpty(_batches));
        cut.WaitForAssertion(() => Assert.False(GetField<bool>(cut.Instance, "_isBatchLoading")));
        return cut;
    }

    private static List<string> Displayed(IRenderedComponent<RoomGalleryGrid> cut) =>
        GetField<List<RoomMediaItemViewModel>>(cut.Instance, "_displayedMedia")!.Select(m => m.MediaId).ToList();

    private static string Markup(IRenderedComponent<RoomGalleryGrid> cut) => WebUtility.HtmlDecode(cut.Markup);

    private static Task Scroll(IRenderedComponent<RoomGalleryGrid> cut) =>
        cut.InvokeAsync(() => cut.Instance.OnSentinelIntersected());

    private static List<string> Range(int from, int to) => Enumerable.Range(from, to - from + 1).Select(Mxc).ToList();

    // ---- Phase 1: what must not change -------------------------------------------------------------

    [Fact]
    public void Unfiltered_FirstLoad_LooksUpOnlyTheDisplayedItems()
    {
        var cut = RenderGrid(21);

        Assert.Equal([Range(1, 20)], _batches);
        Assert.Equal(Range(1, 20), Displayed(cut));
    }

    [Fact]
    public async Task Unfiltered_Scrolling_LooksUpTheNextDisplayedPage()
    {
        var cut = RenderGrid(21);

        await Scroll(cut);

        Assert.Equal([Range(1, 20), [Mxc(21)]], _batches);
        Assert.Equal(Range(1, 21), Displayed(cut));
        Assert.Contains($"{L["AllLoaded"].Value} (21 / 21)", Markup(cut));
    }

    [Fact]
    public async Task SourceFilter_LooksUpOnlyTheNewlyDisplayedItems()
    {
        var cut = RenderGrid(21, remote: ["mxc://other.org/r1", "mxc://other.org/r2"]);

        await InvokeHandler(cut, "OnSourceFilterChanged", "remote");

        Assert.Equal([Range(1, 20), ["mxc://other.org/r1", "mxc://other.org/r2"]], _batches);
        Assert.Equal(["mxc://other.org/r1", "mxc://other.org/r2"], Displayed(cut));
    }

    [Fact]
    public async Task Search_MatchingTheMxcUri_FindsAnItemThatWasNotLookedUp()
    {
        var cut = RenderGrid(21);

        await InvokeHandler(cut, "OnSearchTermChanged", "m21");

        Assert.Equal([Mxc(21)], Displayed(cut));
        Assert.Equal([Range(1, 20), [Mxc(21)]], _batches);
    }
}
