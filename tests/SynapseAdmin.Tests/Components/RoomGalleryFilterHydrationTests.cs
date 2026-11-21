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

    // ---- Phase 2: filters check items without metadata before excluding them -----------------------

    /// <summary>The issue's scenario: 21 items, the first 20 looked up, only item 21 is an image named <see cref="Needle"/>.</summary>
    [Fact]
    public async Task ImagesFilter_LooksUpAndFindsTheOnlyImageBeyondTheFirstPage()
    {
        var cut = RenderGrid(21, needleAt: 21);

        await InvokeHandler(cut, "OnTypeFilterChanged", "image");

        Assert.Equal([Mxc(21)], Displayed(cut));
        Assert.Equal([Range(1, 20), [Mxc(21)]], _batches);
    }

    [Fact]
    public async Task FileNameSearch_LooksUpAndFindsTheOnlyMatchBeyondTheFirstPage()
    {
        var cut = RenderGrid(21, needleAt: 21);

        await InvokeHandler(cut, "OnSearchTermChanged", "needle");

        Assert.Equal([Mxc(21)], Displayed(cut));
        Assert.Equal([Range(1, 20), [Mxc(21)]], _batches);
    }

    /// <summary>Files lists only items whose type is known and is not an image, video or audio.</summary>
    [Fact]
    public async Task FilesFilter_ExcludesAnImageThatWasNotLookedUpYet()
    {
        var cut = RenderGrid(21, needleAt: 21);

        await InvokeHandler(cut, "OnTypeFilterChanged", "file");
        await Scroll(cut);

        Assert.Equal(Range(1, 20), Displayed(cut));
    }

    [Fact]
    public async Task FilesFilter_ExcludesAnItemWhoseLookupFindsNoType()
    {
        const string Uncached = "mxc://other.org/uncached";
        var cut = RenderGrid(2, remote: [Uncached]);
        await InvokeHandler(cut, "OnSourceFilterChanged", "all");
        Assert.Equal([Mxc(1), Mxc(2), Uncached], Displayed(cut));

        await InvokeHandler(cut, "OnTypeFilterChanged", "file");

        Assert.Equal([Mxc(1), Mxc(2)], Displayed(cut));
        Assert.Equal(1, GetField<int>(cut.Instance, "_unknownTypeCount"));
    }

    /// <summary>One load looks up at most 5 batches of 20 items, even if it found no match yet.</summary>
    [Fact]
    public async Task TypeFilter_LooksUpAtMostFiveBatchesPerLoad()
    {
        var cut = RenderGrid(150);

        await InvokeHandler(cut, "OnTypeFilterChanged", "image");

        Assert.Equal([Range(1, 20), Range(21, 40), Range(41, 60), Range(61, 80), Range(81, 100), Range(101, 120)], _batches);
        Assert.Empty(Displayed(cut));
        Assert.True(GetField<bool>(cut.Instance, "_lookupCapReached"));
        Assert.True(GetField<bool>(cut.Instance, "_hasMoreToLoad"));
    }

    /// <summary>A filter change while an older filter's lookup is running: the older filter's result is discarded.</summary>
    [Fact]
    public async Task FilterChange_DuringALookup_DiscardsTheOlderFiltersResult()
    {
        var cut = RenderGrid(41, needleAt: 30);
        var held = Pending<OperationResult<List<RoomMediaItemViewModel>>>();
        _heldBatch = held;

        var images = InvokeHandler(cut, "OnTypeFilterChanged", "image");
        cut.WaitForAssertion(() => Assert.Equal(2, _batches.Count));
        await InvokeHandler(cut, "OnTypeFilterChanged", "all");
        Assert.Equal(Range(1, 20), Displayed(cut));
        held.SetResult(Answer(_batches[1]));
        await images;

        Assert.Equal(Range(1, 20), Displayed(cut));
        Assert.Equal(2, _batches.Count);
        Assert.Equal("image/png", GetField<List<RoomMediaItemViewModel>>(cut.Instance, "_allMedia")!.Single(m => m.MediaId == Mxc(30)).MediaType);
    }

    // ---- Phase 3: the status says when the answer is not complete ----------------------------------

    private void AssertChecking(IRenderedComponent<RoomGalleryGrid> cut, int checkedCount, int total)
    {
        var markup = Markup(cut);
        Assert.Contains(L["GalleryCheckedItems", checkedCount, total].Value, markup);
        Assert.Single(CheckMoreButtons(cut));
        Assert.DoesNotContain(L["NoMediaFound"].Value, markup);
        Assert.DoesNotContain(L["AllLoaded"].Value, markup);
    }

    private IEnumerable<AngleSharp.Dom.IElement> CheckMoreButtons(IRenderedComponent<RoomGalleryGrid> cut) =>
        cut.FindAll("button").Where(b => WebUtility.HtmlDecode(b.TextContent).Trim() == L["GalleryCheckMore"].Value);

    [Fact]
    public async Task LookupCap_ShowsTheCheckedCountAndCheckMore_InsteadOfNoMediaFound()
    {
        var cut = RenderGrid(150);

        await InvokeHandler(cut, "OnTypeFilterChanged", "image");

        AssertChecking(cut, 120, 150);
    }

    [Fact]
    public async Task CheckMore_ChecksTheRest_ThenSaysNoMediaFound()
    {
        var cut = RenderGrid(150);
        await InvokeHandler(cut, "OnTypeFilterChanged", "image");

        await cut.InvokeAsync(() => CheckMoreButtons(cut).Single().Click());
        cut.WaitForAssertion(() => Assert.Contains(L["NoMediaFound"].Value, Markup(cut)));

        Assert.Equal([Range(121, 140), Range(141, 150)], _batches.Skip(6));
        Assert.Empty(CheckMoreButtons(cut));
        Assert.DoesNotContain(L["GalleryCheckedItems", 150, 150].Value, Markup(cut));
    }

    [Fact]
    public async Task CheckMore_FindsAMatchBeyondTheCap_ThenSaysAllLoaded()
    {
        var cut = RenderGrid(150, needleAt: 130);
        await InvokeHandler(cut, "OnTypeFilterChanged", "image");
        AssertChecking(cut, 120, 150);

        await cut.InvokeAsync(() => CheckMoreButtons(cut).Single().Click());
        cut.WaitForAssertion(() => Assert.Equal([Mxc(130)], Displayed(cut)));
        // The page of matches isn't full, so the rest is checked too before the result is final.
        cut.WaitForAssertion(() => Assert.Contains($"{L["AllLoaded"].Value} (1 / 1)", Markup(cut)));
        Assert.Empty(CheckMoreButtons(cut));
    }

    [Fact]
    public async Task TypeFilter_SaysHowManyItemsOfUnknownTypeItHides()
    {
        var cut = RenderGrid(2, remote: ["mxc://other.org/u1", "mxc://other.org/u2"]);
        await InvokeHandler(cut, "OnSourceFilterChanged", "all");

        await InvokeHandler(cut, "OnTypeFilterChanged", "file");

        Assert.Contains(L["GalleryUnknownTypeHidden", 2].Value, Markup(cut));
        await InvokeHandler(cut, "OnTypeFilterChanged", "all");
        Assert.DoesNotContain(L["GalleryUnknownTypeHidden", 2].Value, Markup(cut));
    }

    [Fact]
    public void Unfiltered_ShowsNoCheckingStatus()
    {
        var cut = RenderGrid(21);

        Assert.Empty(CheckMoreButtons(cut));
        Assert.DoesNotContain(L["GalleryCheckedItems", 20, 21].Value, Markup(cut));
    }

    /// <summary>
    /// A load started by the infinite-scroll sentinel (JS) renders its final status by itself, because nothing else
    /// re-renders the grid after a JS call: here it stops at the lookup limit again and must offer "Check more".
    /// </summary>
    [Fact]
    public async Task SentinelLoad_StoppingAtTheCap_RendersCheckMore()
    {
        var cut = RenderGrid(250);
        await InvokeHandler(cut, "OnTypeFilterChanged", "image");

        await Scroll(cut);

        Assert.Equal(11, _batches.Count);
        AssertChecking(cut, 220, 250);
    }
}
