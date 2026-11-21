using System.Net;
using Bunit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Issue #524: the room gallery page with the real <see cref="RoomService"/>. When the homeserver answers the room's
/// media list with <c>null</c>, the room still loads once: the grid gets an empty media model, so it doesn't load
/// the room (details, members, state and media) a second time on its own.
/// </summary>
public class RoomGalleryNullMediaTests : DetailPageTestBase
{
    private const string RoomId = "!room:hs";
    private readonly Stub _gateway;

    public RoomGalleryNullMediaTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(out _gateway);
        _gateway
            .On("get_SupportsAdminApi", _ => true)
            .On("get_HomeserverUrl", _ => "https://hs")
            .On(nameof(IMatrixGateway.GetRoomDetailsAsync), _ => Task.FromResult<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?>(
                new() { RoomId = RoomId, Name = "Alpha Lounge" }))
            .On(nameof(IMatrixGateway.GetRoomMembersAsync), _ => Task.FromResult<SynapseAdminRoomMemberListResult?>(new()))
            .On(nameof(IMatrixGateway.GetRoomStateAsync), _ => Task.FromResult<SynapseAdminRoomStateResult?>(new()))
            .On(nameof(IMatrixGateway.GetRoomMediaListAsync), _ => Task.FromResult<SynapseAdminRoomMediaListResult?>(null));
        var session = Stub.Create<IMatrixSessionService>(s => s
            .On("get_Gateway", _ => gateway)
            .On("get_IsLoggedIn", _ => true));
        Services.AddSingleton(session);
        Services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        Services.AddSingleton<IRoomService>(sp => new RoomService(session, NullLogger<RoomService>.Instance,
            sp.GetRequiredService<IStringLocalizer<SharedResources>>(), sp.GetRequiredService<IMemoryCache>()));
    }

    [Fact]
    public async Task NullMediaList_LoadsTheRoomOnce_AndShowsNoMedia()
    {
        var cut = Render<RoomGallery>(p => p.Add(x => x.RoomId, RoomId));
        var noMedia = Services.GetRequiredService<IStringLocalizer<SharedResources>>()["NoMediaFound"].Value;

        cut.WaitForAssertion(() => Assert.Contains(noMedia, WebUtility.HtmlDecode(cut.Markup)));
        await Task.Delay(200); // a second load would start right after the first render of the grid

        Assert.Contains("Alpha Lounge", cut.Markup);
        foreach (var method in new[]
                 {
                     nameof(IMatrixGateway.GetRoomDetailsAsync), nameof(IMatrixGateway.GetRoomMembersAsync),
                     nameof(IMatrixGateway.GetRoomStateAsync), nameof(IMatrixGateway.GetRoomMediaListAsync)
                 })
        {
            Assert.True(_gateway.CallsTo(method).Count == 1, $"{method} was called {_gateway.CallsTo(method).Count} times");
        }
        Assert.Empty(SnackbarStub.CallsTo("Add"));
    }
}
