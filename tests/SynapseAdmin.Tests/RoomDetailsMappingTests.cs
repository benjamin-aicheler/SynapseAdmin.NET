using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the <see cref="RoomService"/> mappings behind the room details page.
/// <see cref="RoomService.GetRoomMessagesAsync"/>: how each shape of event <c>content</c> maps to
/// <c>Content</c> (raw JSON text) and <c>Body</c>.
/// </summary>
public sealed class RoomDetailsMappingTests : IDisposable
{
    private const string RoomId = "!room:example.com";
    private const string Sender = "@alice:example.com";
    private static readonly DateTimeOffset Timestamp = new(2023, 11, 14, 22, 13, 20, TimeSpan.Zero); // 1700000000000 ms

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly RoomService _rooms;
    private SynapseAdminRoomMessagesResponse _messages = new();

    public RoomDetailsMappingTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_HomeserverUrl", _ => "https://example.com")
            .On(nameof(IMatrixGateway.GetRoomMessagesAsync), _ => Task.FromResult<SynapseAdminRoomMessagesResponse?>(_messages)));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var localizer = Stub.Create<IStringLocalizer<SharedResources>>(s =>
            s.On("get_Item", args => new LocalizedString((string)args[0]!, (string)args[0]!)));
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

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

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
