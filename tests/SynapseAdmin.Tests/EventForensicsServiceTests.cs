using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of <see cref="EventForensicsService.FetchEventAsync"/>: locks in the exact indented
/// <c>RawJson</c> (2-space indent, property order, default escaping of HTML-sensitive and non-ASCII characters).
/// </summary>
public sealed class EventForensicsServiceTests
{
    // Compact input, deserialized the same way the gateway does it, so Event is a parsed JsonObject.
    // The body uses a lower-case JSON escape (\u00e4) on purpose: the serializer re-escapes it upper-case.
    private const string FetchEventJson =
        """{"event":{"auth_events":["$auth:example.com"],"content":{"body":"<b>H\u00e4llo</b> & 'x'","msgtype":"m.text"},"depth":12,"event_id":"$event:example.com","origin_server_ts":1700000000000,"room_id":"!room:example.com","sender":"@alice:example.com","type":"m.room.message","unsigned":{"age_ts":1700000000001}}}""";

    private const string ExpectedRawJson =
        """
        {
          "event": {
            "auth_events": [
              "$auth:example.com"
            ],
            "content": {
              "body": "\u003Cb\u003EH\u00E4llo\u003C/b\u003E \u0026 \u0027x\u0027",
              "msgtype": "m.text"
            },
            "depth": 12,
            "event_id": "$event:example.com",
            "origin_server_ts": 1700000000000,
            "room_id": "!room:example.com",
            "sender": "@alice:example.com",
            "type": "m.room.message",
            "unsigned": {
              "age_ts": 1700000000001
            }
          }
        }
        """;

    [Fact]
    public async Task FetchEvent_ProducesExactIndentedRawJson_OnEveryCall()
    {
        var service = CreateService();
        // The serializer indents with Environment.NewLine; the source file uses LF.
        var expected = ExpectedRawJson.ReplaceLineEndings();

        // Twice: the second call proves that reusing the serializer options doesn't change the output.
        var first = await service.FetchEventAsync("$event:example.com");
        var second = await service.FetchEventAsync("$event:example.com");

        Assert.True(first.Success);
        Assert.Equal(expected, first.Data!.RawJson);
        Assert.True(second.Success);
        Assert.Equal(expected, second.Data!.RawJson);
    }

    private static EventForensicsService CreateService()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_SupportsAdminApi", _ => true)
            .On("FetchEventAsync", _ => Task.FromResult(JsonSerializer.Deserialize<SynapseAdminFetchEventResponse>(FetchEventJson))));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var localizer = new KeyLocalizer();

        return new EventForensicsService(session, NullLogger<EventForensicsService>.Instance, localizer);
    }
}
