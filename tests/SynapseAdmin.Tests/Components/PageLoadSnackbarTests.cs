using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Layout;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which results the room gallery page, the event forensics page and the navigation menu's cookie sync report in a
/// snackbar: every result except a silent cancellation, apart from a successful cookie sync, which reloads the page.
/// </summary>
public class PageLoadSnackbarTests : DetailPageTestBase
{
    private OperationResult<RoomDetailViewModel> _room = new();
    private OperationResult<EventForensicsViewModel> _event = new();
    private OperationResult _cookie = new();
    private readonly Stub _cookieService;
    private readonly Stub _rooms;

    public PageLoadSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms.On(nameof(IRoomService.GetRoomDetailsAsync), _ => Task.FromResult(_room));
        Services.AddSingleton(Stub.Create<IEventForensicsService>(s => s
            .On(nameof(IEventForensicsService.FetchEventAsync), _ => Task.FromResult(_event))));
        // A Blazor session without the auth cookie, on an origin that can store a secure cookie (localhost).
        Services.AddSingleton(Stub.Create<IMatrixAuthStateService>(s => s
            .On(nameof(IMatrixAuthStateService.GetUserId), _ => "@a:hs")
            .On(nameof(IMatrixAuthStateService.GetUsername), _ => "a")
            .On(nameof(IMatrixAuthStateService.GetAccessToken), _ => "token")
            .On(nameof(IMatrixAuthStateService.GetHomeserver), _ => "https://hs")));
        Services.AddSingleton(Stub.Create<IHttpContextAccessor>());
        Services.AddSingleton(Stub.Create<IAuthCookieService>(out _cookieService));
        _cookieService.On(nameof(IAuthCookieService.EstablishCookieSessionAsync), _ => Task.FromResult(_cookie));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    /// <summary>
    /// Issue #480: without a room the page renders no gallery, which would load the room again and report the same
    /// result a second time.
    /// </summary>
    [Theory]
    [InlineData("success", true)]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    public void RoomGalleryLoad_ReportsEveryResultWithoutARoomExceptACancellation(string kind, bool shown)
    {
        _room = Make<OperationResult<RoomDetailViewModel>>(kind);

        Render<RoomGallery>(p => p.Add(x => x.RoomId, "!a:hs"));

        AssertSnackbars(shown ? [_room] : []);
        Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetRoomDetailsAsync)));
    }

    /// <summary>
    /// Issue #480: like the room details page, the gallery says that the room wasn't found (with the reported message,
    /// if one was shown) and links back to the room list, instead of showing an empty gallery.
    /// </summary>
    [Theory]
    [InlineData("success", true)]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    public void RoomGalleryLoad_WithoutARoom_ShowsRoomNotFoundInsteadOfTheGallery(string kind, bool shown)
    {
        _room = Make<OperationResult<RoomDetailViewModel>>(kind);

        var cut = Render<RoomGallery>(p => p.Add(x => x.RoomId, "!a:hs"));

        var markup = WebUtility.HtmlDecode(cut.Markup);
        Assert.Contains(shown ? _room.Message : L["RoomNotFound"].Value, markup);
        Assert.DoesNotContain(L["NoMediaFound"].Value, markup);
        Assert.Equal("rooms", cut.Find("a.mud-button-root").GetAttribute("href"));
    }

    [Fact]
    public void RoomGalleryLoad_WithARoom_ShowsTheGallery_AndLoadsTheRoomOnce()
    {
        _room = OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel { RoomId = "!a:hs", Name = "Alpha Lounge", Media = new RoomMediaViewModel() });

        var cut = Render<RoomGallery>(p => p.Add(x => x.RoomId, "!a:hs"));

        var markup = WebUtility.HtmlDecode(cut.Markup);
        Assert.Contains("Alpha Lounge", markup);
        Assert.Contains(L["NoMediaFound"].Value, markup);
        Assert.DoesNotContain(L["RoomNotFound"].Value, markup);
        Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetRoomDetailsAsync)));
        AssertSnackbars();
    }

    /// <summary>Switching to a room that can't be loaded drops the previous room's name and gallery.</summary>
    [Fact]
    public void RoomGalleryLoad_SwitchingToAMissingRoom_DropsThePreviousRoom()
    {
        _room = OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel { RoomId = "!a:hs", Name = "Alpha Lounge", Media = new RoomMediaViewModel() });
        var cut = Render<RoomGallery>(p => p.Add(x => x.RoomId, "!a:hs"));
        _room = Make<OperationResult<RoomDetailViewModel>>("failure");

        cut.Render(p => p.Add(x => x.RoomId, "!b:hs"));

        var markup = WebUtility.HtmlDecode(cut.Markup);
        Assert.DoesNotContain("Alpha Lounge", markup);
        Assert.Contains(_room.Message, markup);
        Assert.DoesNotContain(L["NoMediaFound"].Value, markup);
    }

    [Theory]
    [InlineData("success", true)]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    public void EventLoad_ReportsEveryResultWithoutAnEventExceptACancellation(string kind, bool shown)
    {
        _event = Make<OperationResult<EventForensicsViewModel>>(kind);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/event-forensics?eventId=%24e");

        Render<EventForensics>();

        AssertSnackbars(shown ? [_event] : []);
    }

    [Theory]
    [InlineData("success", false)]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    public void CookieSync_ReportsEveryFailureExceptACancellation(string kind, bool shown)
    {
        _cookie = Make<OperationResult>(kind);
        AddAuthorization();

        Render<NavMenu>();

        AssertSnackbars(shown ? [_cookie] : []);
        Assert.Single(_cookieService.CallsTo(nameof(IAuthCookieService.EstablishCookieSessionAsync)));
    }
}
