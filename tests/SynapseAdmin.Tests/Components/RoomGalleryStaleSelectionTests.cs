using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Issue #495: Blazor reuses the room gallery page when only the room changes. A room change cancels the previous
/// room's load, and a late answer for it changes nothing: no name, media, alert or snackbar, and the page keeps
/// loading until the current room's answer arrives.
/// </summary>
public class RoomGalleryStaleSelectionTests : DetailPageTestBase
{
    private const string RoomA = "!a:hs";
    private const string RoomB = "!b:hs";

    /// <summary>Every room load, in call order. Each one stays pending until the test answers it.</summary>
    private readonly List<(string RoomId, CancellationToken Token, TaskCompletionSource<OperationResult<RoomDetailViewModel>> Result)> _loads = [];

    public RoomGalleryStaleSelectionTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(s => s
            .On(nameof(IRoomService.GetRoomDetailsAsync), args =>
            {
                var load = Pending<OperationResult<RoomDetailViewModel>>();
                _loads.Add(((string)args[0]!, (CancellationToken)args[1]!, load));
                return load.Task;
            })
            .On(nameof(IRoomService.GetMediaMetadataBatchAsync), args => Task.FromResult(
                OperationResult<List<RoomMediaItemViewModel>>.Ok(
                    ((List<string>)args[0]!).Select(id => new RoomMediaItemViewModel { MediaId = id }).ToList())))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static OperationResult<RoomDetailViewModel> Loaded(string roomId, string name, string mediaId) =>
        OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel
        {
            RoomId = roomId,
            Name = name,
            Media = new RoomMediaViewModel { Local = [new RoomMediaItemViewModel { MediaId = mediaId, IsLocal = true }] }
        });

    private IRenderedComponent<RoomGallery> RenderRoom(string roomId) =>
        Render<RoomGallery>(p => p.Add(x => x.RoomId, roomId));

    private static void Navigate(IRenderedComponent<RoomGallery> cut, string roomId) =>
        cut.Render(p => p.Add(x => x.RoomId, roomId));

    /// <summary>
    /// Answers a load and waits until the page has handled the answer. The page renders once more when the parameter
    /// change that started the load finishes, whether it used the answer or discarded it.
    /// </summary>
    private static void Answer(
        IRenderedComponent<RoomGallery> cut,
        TaskCompletionSource<OperationResult<RoomDetailViewModel>> load,
        OperationResult<RoomDetailViewModel> result)
    {
        var renders = cut.RenderCount;
        load.SetResult(result);
        cut.WaitForState(() => cut.RenderCount > renders);
    }

    private static string Markup(IRenderedComponent<RoomGallery> cut) => WebUtility.HtmlDecode(cut.Markup);

    /// <summary>Asserts that the page shows <paramref name="name"/> and a grid for <paramref name="roomId"/> with only <paramref name="mediaId"/>.</summary>
    private static void AssertShows(IRenderedComponent<RoomGallery> cut, string name, string roomId, string mediaId)
    {
        Assert.Contains(name, Markup(cut));
        var grid = cut.FindComponent<RoomGalleryGrid>().Instance;
        Assert.Equal(roomId, grid.RoomId);
        Assert.Equal(mediaId, Assert.Single(grid.Media!.Local).MediaId);
    }

    private void AssertStillLoading(IRenderedComponent<RoomGallery> cut)
    {
        Assert.Contains(L["Loading"].Value, Markup(cut));
        Assert.Empty(cut.FindComponents<RoomGalleryGrid>());
        Assert.Empty(cut.FindAll(".mud-alert"));
    }

    [Fact]
    public void SwitchingRooms_CancelsThePreviousRoomsLoad()
    {
        var cut = RenderRoom(RoomA);
        Navigate(cut, RoomB);

        Assert.Equal([RoomA, RoomB], _loads.Select(l => l.RoomId));
        Assert.True(_loads[0].Token.IsCancellationRequested);
        Assert.False(_loads[1].Token.IsCancellationRequested);
    }

    /// <summary>
    /// A answers after B was selected: while B is still loading, or after B is shown. Either way A's answer changes
    /// nothing, and B's answer shows B.
    /// </summary>
    [Theory]
    [InlineData("success", false)]
    [InlineData("success", true)]
    [InlineData("failure", false)]
    [InlineData("failure", true)]
    public void LateAnswerForThePreviousRoom_ChangesNothing(string previousRoomResult, bool currentRoomAnswersFirst)
    {
        var cut = RenderRoom(RoomA);
        Navigate(cut, RoomB);
        var (previous, current) = (_loads[0].Result, _loads[1].Result);
        var lateAnswer = previousRoomResult == "success"
            ? Loaded(RoomA, "Alpha Lounge", "mxc://hs/media-a")
            : Make<OperationResult<RoomDetailViewModel>>(previousRoomResult);

        if (currentRoomAnswersFirst)
        {
            Answer(cut, current, Loaded(RoomB, "Bravo Lounge", "mxc://hs/media-b"));
            Answer(cut, previous, lateAnswer);
        }
        else
        {
            Answer(cut, previous, lateAnswer);
            AssertStillLoading(cut);
            Answer(cut, current, Loaded(RoomB, "Bravo Lounge", "mxc://hs/media-b"));
        }

        AssertShows(cut, "Bravo Lounge", RoomB, "mxc://hs/media-b");
        Assert.DoesNotContain("Alpha Lounge", Markup(cut));
        Assert.Empty(cut.FindAll(".mud-alert"));
        AssertSnackbars();
    }

    /// <summary>
    /// Back to A while A's first load is still pending: the room ID matches again, so only the cancelled token tells
    /// that load's answer apart from the current one.
    /// </summary>
    [Fact]
    public void ReturningToTheFirstRoom_DiscardsItsEarlierLoad()
    {
        var cut = RenderRoom(RoomA);
        Navigate(cut, RoomB);
        Navigate(cut, RoomA);
        Assert.Equal([RoomA, RoomB, RoomA], _loads.Select(l => l.RoomId));

        Answer(cut, _loads[2].Result, Loaded(RoomA, "Alpha Lounge", "mxc://hs/media-a"));
        Answer(cut, _loads[0].Result, Loaded(RoomA, "Former Name", "mxc://hs/media-a-before"));
        Answer(cut, _loads[1].Result, Loaded(RoomB, "Bravo Lounge", "mxc://hs/media-b"));

        AssertShows(cut, "Alpha Lounge", RoomA, "mxc://hs/media-a");
        Assert.DoesNotContain("Former Name", Markup(cut));
        Assert.DoesNotContain("Bravo Lounge", Markup(cut));
    }

    [Fact]
    public async Task DisposingThePage_CancelsAndDiscardsThePendingLoad()
    {
        var cut = RenderRoom(RoomA);
        await DisposeComponentsAsync();

        var load = Assert.Single(_loads);
        Assert.True(load.Token.IsCancellationRequested);

        load.Result.SetResult(Make<OperationResult<RoomDetailViewModel>>("failure"));
        // The answer queued the rest of the page's load on the dispatcher, so this runs after it.
        await cut.InvokeAsync(() => { });
        AssertSnackbars();
    }
}
