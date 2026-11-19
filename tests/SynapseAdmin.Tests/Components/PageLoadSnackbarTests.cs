using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Components.Layout;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
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

    public PageLoadSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(s => s
            .On(nameof(IRoomService.GetRoomDetailsAsync), _ => Task.FromResult(_room))));
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

    /// <summary>
    /// Without a room, the page also renders the gallery without media, which loads the room again and reports the
    /// same result a second time.
    /// </summary>
    [Theory]
    [InlineData("success", true)]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    public void RoomGalleryLoad_ReportsEveryResultWithoutARoomExceptACancellation(string kind, bool shown)
    {
        _room = Make<OperationResult<RoomDetailViewModel>>(kind);

        Render<RoomGallery>(p => p.Add(x => x.RoomId, "!a:hs"));

        AssertSnackbars(shown ? [_room, _room] : []);
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
