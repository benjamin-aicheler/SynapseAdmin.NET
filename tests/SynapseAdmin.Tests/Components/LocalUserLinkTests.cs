using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which user IDs the room members table and the event forensics sender link to <c>/users/…</c>: only IDs that end in
/// <c>:</c> + the session's server name, compared case-insensitively. Without a server name, nothing is linked.
/// </summary>
public class LocalUserLinkTests : DetailPageTestBase
{
    private string? _serverName;
    private string _userId = string.Empty;

    public LocalUserLinkTests()
    {
        // Replaces the base class's session: the gateway reports the current _serverName.
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_SupportsAdminApi", _ => true)
            .On("get_ServerName", _ => _serverName));
        Services.AddSingleton(Stub.Create<IMatrixSessionService>(s => s
            .On("get_Gateway", _ => gateway)
            .On("get_IsLoggedIn", _ => true)));
        Services.AddSingleton(Stub.Create<IRoomService>(s => s
            .On(nameof(IRoomService.GetRoomDetailsAsync), _ => Task.FromResult(OperationResult<RoomDetailViewModel>.Ok(
                new RoomDetailViewModel { RoomId = "!a:hs", Name = "Alpha Lounge", Members = [new() { UserId = _userId, DisplayName = "Member" }] })))
            .On(nameof(IRoomService.GetRoomMessagesAsync), _ =>
                Task.FromResult(OperationResult<RoomMessagesViewModel>.Ok(new RoomMessagesViewModel())))));
        Services.AddSingleton(Stub.Create<IEventForensicsService>(s => s
            .On(nameof(IEventForensicsService.FetchEventAsync), _ => Task.FromResult(OperationResult<EventForensicsViewModel>.Ok(
                new EventForensicsViewModel { EventId = "$e", Type = "m.room.message", Sender = _userId, RawJson = "{}" })))));
    }

    public static TheoryData<string, string?, bool> Cases => new()
    {
        { "@a:example.org", "example.org", true },
        { "@a:EXAMPLE.org", "example.org", true },
        { "@a:example.org", "Example.ORG", true },
        { "@a:example.org.evil", "example.org", false }, // the server name must be the whole suffix
        { "@a:evilexample.org", "example.org", false },  // ... after a ':'
        { "@a:other.org", "example.org", false },
        { "@a:example.org", "", false },
        { "@a:example.org", null, false },
        { "@a:", "", false }, // ':' + a missing server name would match
        { "@a:", null, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    [InlineData("", "example.org", false)] // the forensics page never asks for an empty sender
    public void RoomMembers_LinkOnlyLocalUsers(string userId, string? serverName, bool linked)
    {
        (_userId, _serverName) = (userId, serverName);
        var cut = Render<RoomDetails>(p => p.Add(x => x.RoomId, "!a:hs"));
        cut.WaitForAssertion(() => Assert.Contains("Alpha Lounge", cut.Markup));

        var membersLabel = Services.GetRequiredService<IStringLocalizer<SharedResources>>()["Members"].Value;
        cut.FindAll(".mud-tab").First(t => t.TextContent.Trim() == membersLabel).Click();
        cut.WaitForAssertion(() => Assert.Contains("Member", cut.Find("tbody").TextContent));

        AssertUserLinks(cut, userId, linked);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EventSender_IsLinkedOnlyWhenLocal(string userId, string? serverName, bool linked)
    {
        (_userId, _serverName) = (userId, serverName);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/event-forensics?eventId=%24e");
        var cut = Render<EventForensics>();
        cut.WaitForAssertion(() => Assert.Contains("m.room.message", cut.Markup));

        AssertUserLinks(cut, userId, linked);
    }

    private static void AssertUserLinks<T>(IRenderedComponent<T> cut, string userId, bool linked) where T : IComponent
    {
        var userLinks = cut.FindAll("a")
            .Select(a => a.GetAttribute("href"))
            .Where(href => href?.StartsWith("/users/", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(linked, userLinks.Count > 0);
        Assert.All(userLinks, href => Assert.Equal("/users/" + Uri.EscapeDataString(userId), href));
    }
}
