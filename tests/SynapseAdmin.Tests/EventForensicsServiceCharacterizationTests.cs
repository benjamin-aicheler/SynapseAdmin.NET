using System.Text.Json;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the result and logging contract of <see cref="EventForensicsService.FetchEventAsync"/>
/// (P3-11g): the order of its three checks, the trimmed event ID, cancellation, gateway failure, a missing event
/// and success. <see cref="EventForensicsServiceTests"/> covers the raw JSON.
/// </summary>
public sealed class EventForensicsServiceCharacterizationTests : IDisposable
{
    // The service trims the ID before it logs it: sanitizing alone would log the surrounding tabs as '?'. The line
    // break proves that the log calls sanitize the ID.
    private const string EventId = "\t $ev\nent:example.com \t";
    private const string TrimmedEventId = "$ev\nent:example.com";
    private static readonly Dictionary<string, object?> EventArgument = new() { ["EventId"] = "$ev ent:example.com" };

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<EventForensicsService> _logger = new();

    public void Dispose() => _cts.Dispose();

    [Theory]
    [InlineData(EventId)]
    [InlineData("")]
    public async Task NoSession_FailsNotAuthenticated_BeforeTheOtherChecks(string eventId)
    {
        var result = await CreateService(gateway: null).FetchEventAsync(eventId, _cts.Token);

        ResultAssert.Failure(result, "NotAuthenticated");
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData(EventId)]
    [InlineData("")]
    public async Task WithoutAdminApi_FailsBeforeTheEventIdCheck_WithoutFetching(string eventId)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        var result = await CreateService(gateway).FetchEventAsync(eventId, _cts.Token);

        ResultAssert.Failure(result, "EventForensicsApiRequired");
        Assert.Empty(stub.CallsTo("FetchEventAsync"));
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task BlankEventId_Fails_WithoutFetching(string eventId)
    {
        var gateway = AdminGateway(out var stub);

        var result = await CreateService(gateway).FetchEventAsync(eventId, _cts.Token);

        ResultAssert.Failure(result, "EventIdRequired");
        Assert.Empty(stub.CallsTo("FetchEventAsync"));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task FetchesTheTrimmedEventId()
    {
        var gateway = AdminGateway(out var stub);

        await CreateService(gateway).FetchEventAsync(EventId, _cts.Token);

        Assert.Equal(new object?[] { TrimmedEventId, _cts.Token }, Assert.Single(stub.CallsTo("FetchEventAsync")));
    }

    [Fact]
    public async Task GatewayCancels_ReturnsSilentCancellation_WithoutLogging()
    {
        var gateway = AdminGateway(out var stub);
        stub.Fault("FetchEventAsync", new TaskCanceledException());

        var result = await CreateService(gateway).FetchEventAsync(EventId, _cts.Token);

        ResultAssert.Cancelled(result);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task GatewayThrows_Fails_AndLogsTheTrimmedEventId()
    {
        var error = new InvalidOperationException("boom");
        var gateway = AdminGateway(out var stub);
        stub.Fault("FetchEventAsync", error);

        var result = await CreateService(gateway).FetchEventAsync(EventId, _cts.Token);

        ResultAssert.Failure(result, "ErrorFetchingEvent");
        AssertSingleLog(LogLevel.Error, "Error fetching event {EventId}", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public async Task MissingEvent_IsAWarning(string? json)
    {
        var gateway = AdminGateway(out var stub);
        stub.On("FetchEventAsync", _ => Task.FromResult(json == null ? null : JsonSerializer.Deserialize<SynapseAdminFetchEventResponse>(json)));

        var result = await CreateService(gateway).FetchEventAsync(EventId, _cts.Token);

        ResultAssert.Failure(result, "EventNotFound", Severity.Warning);
        AssertSingleLog(LogLevel.Warning, "Event {EventId} not found on server.", exception: null);
    }

    [Theory]
    [InlineData("1700000000000", 1_700_000_000_000L)]
    [InlineData("\"1700000000000\"", null)]
    public async Task Success_MapsTheEvent_WithoutLogging(string ts, long? expectedTs)
    {
        var json = """{"event":{"event_id":"$x:example.com","room_id":"!r:example.com","sender":"@a:example.com","type":"m.room.message","origin_server_ts":TS,"content":{"body":"hi"}}}""".Replace("TS", ts);
        var gateway = AdminGateway(out var stub);
        stub.On("FetchEventAsync", _ => Task.FromResult(JsonSerializer.Deserialize<SynapseAdminFetchEventResponse>(json)));

        var result = await CreateService(gateway).FetchEventAsync(EventId, _cts.Token);

        ResultAssert.Success(result, "EventFetchedSuccessfully");
        var vm = result.Data!;
        Assert.Equal(("$x:example.com", "!r:example.com", "@a:example.com", "m.room.message", expectedTs),
            (vm.EventId, vm.RoomId, vm.Sender, vm.Type, vm.OriginServerTs));
        Assert.Equal("hi", vm.Content!["body"]!.ToString());
        Assert.Null(vm.Unsigned);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Success_WithoutEventIdInTheEvent_UsesTheTrimmedId()
    {
        var gateway = AdminGateway(out var stub);
        stub.On("FetchEventAsync", _ => Task.FromResult(JsonSerializer.Deserialize<SynapseAdminFetchEventResponse>("""{"event":{}}""")));

        var result = await CreateService(gateway).FetchEventAsync(EventId, _cts.Token);

        Assert.Equal(TrimmedEventId, result.Data!.EventId);
    }

    private static IMatrixGateway AdminGateway(out Stub stub)
    {
        var gateway = Stub.Create<IMatrixGateway>(out stub);
        stub.On("get_SupportsAdminApi", _ => true);
        return gateway;
    }

    private EventForensicsService CreateService(IMatrixGateway? gateway)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new EventForensicsService(session, _logger, new KeyLocalizer());
    }

    private void AssertSingleLog(LogLevel level, string template, Exception? exception)
    {
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((level, template), (entry.Level, entry.Template));
        Assert.Equal(EventArgument, entry.Arguments);
        Assert.Same(exception, entry.Exception);
    }
}
