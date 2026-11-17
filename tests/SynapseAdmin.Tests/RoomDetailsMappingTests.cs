using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the <see cref="RoomService"/> mappings behind the room details page.
/// <see cref="RoomService.GetRoomMessagesAsync"/>: how each shape of event <c>content</c> maps to
/// <c>Content</c> (raw JSON text) and <c>Body</c>.
/// <see cref="RoomService.GetRoomDetailsAsync"/>: tombstone, members and state events from the room state.
/// </summary>
public sealed class RoomDetailsMappingTests : IDisposable
{
    private const string RoomId = "!room:example.com";
    private const string Sender = "@alice:example.com";
    private static readonly DateTimeOffset Timestamp = new(2023, 11, 14, 22, 13, 20, TimeSpan.Zero); // 1700000000000 ms

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly RoomService _rooms;
    private SynapseAdminRoomMessagesResponse _messages = new();
    private SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom _room = new() { RoomId = RoomId };
    private SynapseAdminRoomMemberListResult? _members;
    private SynapseAdminRoomStateResult? _state;

    public RoomDetailsMappingTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_HomeserverUrl", _ => "https://example.com")
            .On(nameof(IMatrixGateway.GetRoomMessagesAsync), _ => Task.FromResult<SynapseAdminRoomMessagesResponse?>(_messages))
            .On(nameof(IMatrixGateway.GetRoomDetailsAsync), _ => Task.FromResult<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?>(_room))
            .On(nameof(IMatrixGateway.GetRoomMembersAsync), _ => Task.FromResult(_members))
            .On(nameof(IMatrixGateway.GetRoomStateAsync), _ => Task.FromResult(_state)));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var localizer = new KeyLocalizer();
        _rooms = new RoomService(session, NullLogger<RoomService>.Instance, localizer, _cache);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task RoomMessages_MapEveryContentShape()
    {
        _messages = new SynapseAdminRoomMessagesResponse
        {
            End = "end-token",
            Chunk =
            [
                Event("$member", Json("""{"membership":"join"}"""), type: "m.room.member"), // not a message: filtered out
                Event("$absent", null),
                Event("$null", Json("null")),
                Event("$undefined", default(JsonElement)),
                Event("$number", Json("42")),
                Event("$array", Json("""["a",1]""")),
                Event("$nobody", Json("""{"msgtype":"m.text"}""")),
                Event("$nullbody", Json("""{"body":null}""")),
                Event("$body", Json("""{"msgtype":"m.text","body":"hello"}""")),
            ]
        };

        var result = await _rooms.GetRoomMessagesAsync(RoomId);

        Assert.True(result.Success);
        Assert.Equal("end-token", result.Data!.EndToken);
        Assert.Collection(result.Data.Messages,
            m => AssertMessage(m, "$absent", content: null, body: null),
            m => AssertMessage(m, "$null", content: null, body: null),
            m => AssertMessage(m, "$undefined", content: null, body: null),
            m => AssertMessage(m, "$number", content: "42", body: null),
            m => AssertMessage(m, "$array", content: """["a",1]""", body: null),
            m => AssertMessage(m, "$nobody", content: """{"msgtype":"m.text"}""", body: null),
            m => AssertMessage(m, "$nullbody", content: """{"body":null}""", body: null),
            m => AssertMessage(m, "$body", content: """{"msgtype":"m.text","body":"hello"}""", body: "hello"));
    }

    [Fact]
    public async Task RoomMessages_NonStringBody_FailsTheWholeCall()
    {
        _messages = new SynapseAdminRoomMessagesResponse
        {
            Chunk =
            [
                Event("$body", Json("""{"body":"hello"}""")),
                Event("$numberbody", Json("""{"body":5}""")),
            ]
        };

        var result = await _rooms.GetRoomMessagesAsync(RoomId);

        Assert.False(result.Success);
        Assert.Equal("ErrorFetchingRoomMessages", result.Message);
    }

    [Theory]
    [InlineData(null, null, null, false, null)] // no tombstone anywhere
    [InlineData(null, null, "!state:example.com", true, "!state:example.com")] // only in the room state
    [InlineData(true, null, "!state:example.com", true, "!state:example.com")] // admin API without replacement: state fills it
    [InlineData(true, "!admin:example.com", "!state:example.com", true, "!admin:example.com")] // admin API complete: state ignored
    public async Task RoomDetails_Tombstone(bool? adminTombstoned, string? adminReplacement, string? stateReplacement,
        bool expectedTombstoned, string? expectedReplacement)
    {
        _room = new() { RoomId = RoomId, Tombstoned = adminTombstoned, ReplacementRoom = adminReplacement };
        _state = new() { Events = stateReplacement == null ? [] : [StateEvent("m.room.tombstone", "", $$"""{"replacement_room":"{{stateReplacement}}"}""")] };

        var result = await _rooms.GetRoomDetailsAsync(RoomId);

        Assert.True(result.Success);
        Assert.Equal(expectedTombstoned, result.Data!.IsTombstoned);
        Assert.Equal(expectedReplacement, result.Data.ReplacementRoom);
    }

    [Fact]
    public async Task RoomDetails_MapMembersFromTheirFirstMemberEvent_AndAllStateEvents()
    {
        const string alice = """{"displayname":"Alice","avatar_url":"mxc://example.com/a","membership":"join"}""";
        const string aliceLater = """{"displayname":"Alice later","membership":"leave"}""";
        const string bob = """{"displayname":42,"avatar_url":null,"membership":"invite"}""";
        const string name = """{"name":"Lobby"}""";
        _members = new() { Members = ["@nostate:example.com", "@alice:example.com", "@bob:example.com", "@carol:example.com"] };
        _state = new()
        {
            Events =
            [
                StateEvent("m.room.member", "@alice:example.com", alice),
                StateEvent("m.room.member", "@alice:example.com", aliceLater), // duplicate: the first one wins
                StateEvent("m.room.member", "@bob:example.com", bob),
                StateEvent("m.room.member", "@carol:example.com", null),
                StateEvent("m.room.name", "", name),
            ]
        };

        var result = await _rooms.GetRoomDetailsAsync(RoomId);

        Assert.True(result.Success);
        (string, string?, string?, string?)[] expectedMembers =
        [
            ("@nostate:example.com", null, null, null), // no member event
            ("@alice:example.com", "Alice", "mxc://example.com/a", "join"),
            ("@bob:example.com", "42", null, "invite"), // non-string displayname: its JSON text; JSON null: null
            ("@carol:example.com", null, null, null), // member event without content
        ];
        Assert.Equal(expectedMembers, result.Data!.Members.Select(m => (m.UserId, m.DisplayName, m.AvatarUrl, m.Membership)));
        (string, string?, string?, string?)[] expectedStateEvents =
        [
            ("m.room.member", "@alice:example.com", Sender, alice),
            ("m.room.member", "@alice:example.com", Sender, aliceLater),
            ("m.room.member", "@bob:example.com", Sender, bob),
            ("m.room.member", "@carol:example.com", Sender, null),
            ("m.room.name", "", Sender, name),
        ];
        Assert.Equal(expectedStateEvents, result.Data.StateEvents.Select(e => (e.Type, e.StateKey, e.Sender, e.RawContent)));
    }

    [Fact]
    public async Task RoomDetails_NoMembersAndNoState_AreEmptyLists()
    {
        var result = await _rooms.GetRoomDetailsAsync(RoomId);

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Members);
        Assert.Empty(result.Data.StateEvents);
    }

    [Fact]
    public async Task RoomDetails_MembersWithoutState_HaveOnlyTheirUserId()
    {
        _members = new() { Members = ["@alice:example.com"] };

        var result = await _rooms.GetRoomDetailsAsync(RoomId);

        Assert.True(result.Success);
        var member = Assert.Single(result.Data!.Members);
        Assert.Equal(("@alice:example.com", (string?)null, (string?)null, (string?)null), (member.UserId, member.DisplayName, member.AvatarUrl, member.Membership));
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static MatrixEventResponse StateEvent(string type, string stateKey, string? content) => new()
    {
        Type = type,
        StateKey = stateKey,
        Sender = Sender,
        RawContent = content == null ? null : JsonNode.Parse(content)!.AsObject()
    };

    private static SynapseAdminEvent Event(string eventId, JsonElement? content, string type = "m.room.message") => new()
    {
        EventId = eventId,
        Sender = Sender,
        OriginServerTs = 1700000000000,
        Type = type,
        StateKey = eventId + ":state",
        Content = content
    };

    private static void AssertMessage(RoomMessageItemViewModel message, string eventId, string? content, string? body)
    {
        Assert.Equal(eventId, message.EventId);
        Assert.Equal(Sender, message.Sender);
        Assert.Equal(Timestamp, message.OriginServerTs);
        Assert.Equal("m.room.message", message.Type);
        Assert.Equal(eventId + ":state", message.StateKey);
        Assert.Equal(content, message.Content);
        Assert.Equal(body, message.Body);
    }
}
