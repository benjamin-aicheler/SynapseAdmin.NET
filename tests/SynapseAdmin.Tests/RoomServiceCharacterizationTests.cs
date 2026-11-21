using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;
using Room = SynapseAdmin.Models.Responses.SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the result and logging contract of every <see cref="RoomService"/> method (P3-11f): no
/// session, forwarded arguments, cancellation, gateway failure, null gateway result and success. Mapping, media
/// de-duplication, cache keys and null results have their own suites; this one adds the error handling, the
/// fan-out calls that continue after a failed item, and the cache entries that actions clear.
/// </summary>
public sealed class RoomServiceCharacterizationTests : IDisposable
{
    // The line breaks prove that every log call sanitizes the untrusted strings.
    private const string RoomId = "!room\n1:example.com";
    private const string SanitizedRoomId = "!room 1:example.com";
    private const string Alias = "#alias\n:example.com";
    private const string SanitizedAlias = "#alias :example.com";
    private const string PurgeId = "purge\n1";
    private const string Search = "ali\nce";
    private static readonly Dictionary<string, object?> RoomArgument = new() { ["RoomId"] = SanitizedRoomId };

    private sealed record Operation(
        string GatewayMethod,
        Func<RoomService, CancellationToken, Task<OperationResult>> Invoke,
        Action<object?[], CancellationToken> AssertGatewayArgs,
        string FailureKey,
        string ErrorTemplate,
        Dictionary<string, object?> ErrorArguments,
        string? SuccessKey = null,
        string? InfoTemplate = null,
        Dictionary<string, object?>? InfoArguments = null);

    private static Action<object?[], CancellationToken> Args(params object?[] expected) => (args, ct) => Assert.Equal(expected.Append(ct).ToArray(), args);

    private static readonly Dictionary<string, Operation> Operations = new()
    {
        ["List"] = new("GetRoomListAsync", async (s, ct) => await s.GetRoomListAsync(20, 10, "name", SortDirection.Ascending, Search, ct),
            Args(20, 10, "name", "f", Search), "ErrorFetchingRoomList",
            "Error fetching room list (offset: {Offset}, limit: {Limit}, searchTerm: {SearchTerm})",
            new() { ["Offset"] = 20, ["Limit"] = 10, ["SearchTerm"] = "ali ce" }),
        ["Details"] = new("GetRoomDetailsAsync", async (s, ct) => await s.GetRoomDetailsAsync(RoomId, ct),
            Args(RoomId), "ErrorFetchingRoomDetails", "Error fetching room details for {RoomId}", RoomArgument),
        ["Delete"] = new("DeleteRoomAsync", (s, ct) => s.DeleteRoomAsync(RoomId, block: true, purge: false, ct),
            (args, ct) =>
            {
                Assert.Equal(RoomId, args[0]);
                var request = Assert.IsType<SynapseAdminRoomDeleteRequest>(args[1]);
                Assert.Equal((true, false, (string?)null, (string?)null, (string?)null),
                    (request.Block, request.Purge, request.NewRoomUserId, request.RoomName, request.Message));
                Assert.Equal(ct, args[2]);
            },
            "ErrorDeletingRoom", "Error deleting room {RoomId}", RoomArgument,
            "RoomDeletedSuccessfully", "Successfully deleted room {RoomId} (block: {Block}, purge: {Purge})",
            new() { ["RoomId"] = SanitizedRoomId, ["Block"] = true, ["Purge"] = false }),
        ["QuarantineMedia"] = new("QuarantineMediaByRoomIdAsync", (s, ct) => s.QuarantineMediaAsync(RoomId, ct),
            Args(RoomId), "ErrorQuarantiningRoomMedia", "Error quarantining media for room {RoomId}", RoomArgument,
            "RoomMediaQuarantinedSuccessfully", "Successfully quarantined media for room {RoomId}", RoomArgument),
        ["Block"] = new("BlockRoomAsync", (s, ct) => s.BlockRoomAsync(RoomId, true, ct),
            Args(RoomId, true), "ErrorBlockingRoom", "Error blocking/unblocking room {RoomId}", RoomArgument,
            "RoomBlockedSuccessfully", "Successfully {Action} room {RoomId}", new() { ["Action"] = "blocked", ["RoomId"] = SanitizedRoomId }),
        ["Unblock"] = new("BlockRoomAsync", (s, ct) => s.BlockRoomAsync(RoomId, false, ct),
            Args(RoomId, false), "ErrorUnblockingRoom", "Error blocking/unblocking room {RoomId}", RoomArgument,
            "RoomUnblockedSuccessfully", "Successfully {Action} room {RoomId}", new() { ["Action"] = "unblocked", ["RoomId"] = SanitizedRoomId }),
        ["LargestRooms"] = new("GetLargestRoomsAsync", async (s, ct) => await s.GetLargestRoomsAsync(ct),
            Args(), "ErrorFetchingLargestRooms", "Error fetching largest rooms", []),
        ["Messages"] = new("GetRoomMessagesAsync", async (s, ct) => await s.GetRoomMessagesAsync(RoomId, "from", 5, "b", "filter", "to", ct),
            Args(RoomId, 5, "from", "b", "filter", "to"), "ErrorFetchingRoomMessages", "Error fetching room messages for {RoomId}", RoomArgument),
        ["Purge"] = new("PurgeRoomHistoryAsync", async (s, ct) => await s.PurgeRoomHistoryAsync(RoomId, new() { DeleteLocalEvents = true, PurgeUpToEventId = "$e", PurgeUpToTs = 99 }, ct),
            (args, ct) =>
            {
                Assert.Equal(RoomId, args[0]);
                var request = Assert.IsType<SynapseAdminPurgeHistoryRequest>(args[1]);
                Assert.Equal(((bool?)true, "$e", (long?)99), (request.DeleteLocalEvents, request.PurgeUpToEventId, request.PurgeUpToTs));
                Assert.Equal(ct, args[2]);
            },
            "ErrorPurgingHistory", "Error purging history for room {RoomId}", RoomArgument),
        ["PurgeStatus"] = new("GetPurgeHistoryStatusAsync", async (s, ct) => await s.GetPurgeHistoryStatusAsync(PurgeId, ct),
            Args(PurgeId), "ErrorFetchingPurgeStatus", "Error getting purge status for purge {PurgeId}", new() { ["PurgeId"] = "purge 1" }),
        ["PublicRooms"] = new("GetPublicRoomsAsync", async (s, ct) => await s.GetPublicRoomsAsync(20, "since", Search, ct),
            Args(20, "since", Search), "ErrorFetchingRoomDirectory", "Error fetching public room directory", []),
        ["GetVisibility"] = new("GetRoomDirectoryVisibilityAsync", async (s, ct) => await s.GetRoomDirectoryVisibilityAsync(RoomId, ct),
            Args(RoomId), "ErrorFetchingRoomDirectoryVisibility", "Error fetching directory visibility for room {RoomId}", RoomArgument),
        ["SetVisibility"] = new("SetRoomDirectoryVisibilityAsync", (s, ct) => s.SetRoomDirectoryVisibilityAsync(RoomId, "public", ct),
            Args(RoomId, "public"), "ErrorSettingRoomDirectoryVisibility", "Error setting directory visibility for room {RoomId} to {Visibility}",
            new() { ["RoomId"] = SanitizedRoomId, ["Visibility"] = "public" }, "RoomDirectoryVisibilityUpdated"),
        ["PutAlias"] = new("PutRoomAliasAsync", (s, ct) => s.PutRoomAliasAsync(Alias, RoomId, ct),
            Args(Alias, RoomId), "ErrorSettingRoomAlias", "Error setting alias {Alias} for room {RoomId}",
            new() { ["Alias"] = SanitizedAlias, ["RoomId"] = SanitizedRoomId }, "AliasAddedSuccessfully"),
        ["DeleteAlias"] = new("DeleteRoomAliasAsync", (s, ct) => s.DeleteRoomAliasAsync(Alias, ct),
            Args(Alias), "ErrorDeletingRoomAlias", "Error deleting alias {Alias}", new() { ["Alias"] = SanitizedAlias }, "AliasDeletedSuccessfully"),
        ["LocalAliases"] = new("GetLocalRoomAliasesAsync", async (s, ct) => await s.GetLocalRoomAliasesAsync(RoomId, ct),
            Args(RoomId), "ErrorFetchingRoomAliases", "Error fetching aliases for room {RoomId}", RoomArgument),
    };

    public static TheoryData<string> AllOperations => new(Operations.Keys);
    public static TheoryData<string> SingleCallOperations => new(Operations.Keys.Where(k => k != "Details"));
    public static TheoryData<string> ActionOperations => new(Operations.Where(o => o.Value.SuccessKey != null).Select(o => o.Key));

    private readonly CancellationTokenSource _cts = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly RecordingLogger<RoomService> _logger = new();

    public void Dispose()
    {
        _cts.Dispose();
        _cache.Dispose();
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task NoSession_FailsNotAuthenticated_WithoutLogging(string name)
    {
        var result = await Operations[name].Invoke(CreateService(gateway: null), _cts.Token);

        ResultAssert.Failure(result, "NotAuthenticated");
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task MetadataBatch_NoSession_FailsNotAuthenticated()
    {
        var result = await CreateService(gateway: null).GetMediaMetadataBatchAsync(["mxc://example.com/a"], _cts.Token);

        ResultAssert.Failure(result, "NotAuthenticated");
    }

    [Theory, MemberData(nameof(SingleCallOperations))]
    public async Task ForwardsArgumentsToGateway(string name)
    {
        var operation = Operations[name];
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        await operation.Invoke(CreateService(gateway), _cts.Token);

        var call = Assert.Single(stub.Calls, c => c.Method != "get_HomeserverUrl");
        Assert.Equal(operation.GatewayMethod, call.Method);
        operation.AssertGatewayArgs(call.Args, _cts.Token);
    }

    [Fact]
    public async Task List_SortsRoomIdAlphabetically()
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        await CreateService(gateway).GetRoomListAsync(0, 10, "room_id", SortDirection.None, token: _cts.Token);

        var args = Assert.Single(stub.CallsTo("GetRoomListAsync"));
        Assert.Equal(("alphabetical", "f"), ((string?)args[2], (string?)args[3]));
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayCancels_ReturnsSilentCancellation_WithoutLogging(string name)
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.Fault(Operations[name].GatewayMethod, new TaskCanceledException()));

        var result = await Operations[name].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Cancelled(result);
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayThrows_FailsWithOperationKey_AndLogsError(string name)
    {
        var operation = Operations[name];
        var error = new InvalidOperationException("boom");
        var gateway = Stub.Create<IMatrixGateway>(s => s.Fault(operation.GatewayMethod, error));

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, operation.FailureKey);
        AssertSingleLog(LogLevel.Error, operation.ErrorTemplate, operation.ErrorArguments, error);
    }

    [Theory, MemberData(nameof(ActionOperations))]
    public async Task Action_Success_ReturnsLocalizedMessage_AndLogsInformationIfAny(string name)
    {
        var operation = Operations[name];

        var result = await operation.Invoke(CreateService(Stub.Create<IMatrixGateway>()), _cts.Token);

        ResultAssert.Success(result, operation.SuccessKey!);
        if (operation.InfoTemplate == null) Assert.Empty(_logger.Entries);
        else AssertSingleLog(LogLevel.Information, operation.InfoTemplate, operation.InfoArguments!, exception: null);
    }

    [Theory]
    [InlineData("Details", "RoomNotFound")]
    [InlineData("Purge", "ErrorPurgingHistory")]
    [InlineData("PurgeStatus", "ErrorFetchingPurgeStatus")]
    [InlineData("GetVisibility", "ErrorFetchingRoomDirectoryVisibility")]
    public async Task SingleResource_NullGatewayResult_Fails_WithoutLogging(string name, string key)
    {
        var result = await Operations[name].Invoke(CreateService(Stub.Create<IMatrixGateway>()), _cts.Token);

        ResultAssert.Failure(result, key);
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData("LargestRooms")]
    [InlineData("Messages")]
    public async Task CollectionRead_NullGatewayResult_IsEmpty(string name)
    {
        var result = await Operations[name].Invoke(CreateService(Stub.Create<IMatrixGateway>()), _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Empty(_logger.Entries);
        switch (result)
        {
            case OperationResult<List<RoomStatisticsViewModel>> largest: Assert.Empty(largest.Data!); break;
            case OperationResult<RoomMessagesViewModel> messages: Assert.Equal((0, (string?)null), (messages.Data!.Messages.Count, messages.Data.EndToken)); break;
            default: Assert.Fail($"Unexpected result type {result.GetType()}"); break;
        }
    }

    [Theory]
    [InlineData("private", "RoomRemovedFromDirectory")]
    [InlineData("PRIVATE", "RoomRemovedFromDirectory")]
    [InlineData("public", "RoomDirectoryVisibilityUpdated")]
    public async Task SetVisibility_Success_NamesTheNewVisibility(string visibility, string message)
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).SetRoomDirectoryVisibilityAsync(RoomId, visibility, _cts.Token);

        ResultAssert.Success(result, message);
    }

    [Fact]
    public async Task Delete_Success_ForgetsTheActivePurge_ButAFailureKeepsIt()
    {
        var service = CreateService(Stub.Create<IMatrixGateway>(s => s.Fault("DeleteRoomAsync", new InvalidOperationException())));
        service.SetActivePurgeId(RoomId, "purge");

        await service.DeleteRoomAsync(RoomId, token: _cts.Token);
        Assert.Equal("purge", service.GetActivePurgeId(RoomId));

        await CreateService(Stub.Create<IMatrixGateway>()).DeleteRoomAsync(RoomId, token: _cts.Token);
        Assert.Null(service.GetActivePurgeId(RoomId));
    }

    [Fact]
    public async Task Purge_Success_RemembersThePurgeId()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("PurgeRoomHistoryAsync", _ => Task.FromResult<SynapseAdminPurgeHistoryResponse?>(new() { PurgeId = "p1" })));
        var service = CreateService(gateway);

        var result = await service.PurgeRoomHistoryAsync(RoomId, new PurgeHistoryViewModel(), _cts.Token);

        ResultAssert.Success(result, "PurgeStarted");
        Assert.Equal("p1", result.Data);
        Assert.Equal("p1", service.GetActivePurgeId(RoomId));
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Purge_WithoutPurgeId_SucceedsWithAWarning_AndRemembersNothing(string purgeId)
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("PurgeRoomHistoryAsync", _ => Task.FromResult<SynapseAdminPurgeHistoryResponse?>(new() { PurgeId = purgeId })));
        var service = CreateService(gateway);

        var result = await service.PurgeRoomHistoryAsync(RoomId, new PurgeHistoryViewModel(), _cts.Token);

        ResultAssert.Success(result, "PurgeStarted");
        Assert.Equal("", result.Data);
        Assert.Null(service.GetActivePurgeId(RoomId));
        AssertSingleLog(LogLevel.Warning, "Purge history request for room {RoomId} succeeded without returning a purge_id", RoomArgument, exception: null);
    }

    [Theory]
    [InlineData("PurgeStatus")]
    [InlineData("GetVisibility")]
    [InlineData("LocalAliases")]
    public async Task Read_Success_ReturnsTheGatewayResult_WithoutLogging(string name)
    {
        var status = new SynapseAdminPurgeHistoryStatusResponse { Status = "complete" };
        List<string> aliases = ["#a:example.com"];
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("GetPurgeHistoryStatusAsync", _ => Task.FromResult<SynapseAdminPurgeHistoryStatusResponse?>(status))
            .On("GetRoomDirectoryVisibilityAsync", _ => Task.FromResult<string?>("public"))
            .On("GetLocalRoomAliasesAsync", _ => Task.FromResult<List<string>?>(aliases)));

        var result = await Operations[name].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Success(result, "");
        object? data = result switch
        {
            OperationResult<SynapseAdminPurgeHistoryStatusResponse> r => r.Data,
            OperationResult<string> r => r.Data,
            OperationResult<List<string>> r => r.Data,
            _ => throw new InvalidOperationException(result.GetType().ToString()),
        };
        Assert.Equal(name switch { "PurgeStatus" => status, "GetVisibility" => "public", _ => (object)aliases }, data);
        Assert.Empty(_logger.Entries);
    }

    // A null media list is a collection read: the room has no media, not "media still unknown" (#395, #524).
    [Fact]
    public async Task Details_NullMediaList_IsEmptyMedia()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("GetRoomDetailsAsync", _ => Task.FromResult<Room?>(new Room { RoomId = RoomId }))
            .On("GetRoomMediaListAsync", _ => Task.FromResult<SynapseAdminRoomMediaListResult?>(null)));

        var result = await CreateService(gateway).GetRoomDetailsAsync(RoomId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.NotNull(result.Data!.Media);
        Assert.Empty(result.Data.Media.Local);
        Assert.Empty(result.Data.Media.Remote);
        Assert.Empty(_logger.Entries);
    }

    // Members, state and media have no fallback: a failure fails the whole details call.
    [Theory]
    [InlineData("GetRoomMembersAsync")]
    [InlineData("GetRoomStateAsync")]
    [InlineData("GetRoomMediaListAsync")]
    public async Task Details_SubRequestFails_FailsTheDetails(string method)
    {
        var error = new InvalidOperationException("boom");
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("GetRoomDetailsAsync", _ => Task.FromResult<Room?>(new Room { RoomId = RoomId }))
            .Fault(method, error));

        var result = await CreateService(gateway).GetRoomDetailsAsync(RoomId, _cts.Token);

        ResultAssert.Failure(result, "ErrorFetchingRoomDetails");
        AssertSingleLog(LogLevel.Error, "Error fetching room details for {RoomId}", RoomArgument, error);
    }

    [Fact]
    public async Task MetadataBatch_ItemFails_ReturnsItUnhydrated_AndLogsAtDebug()
    {
        var error = new InvalidOperationException("boom");
        var gateway = Stub.Create<IMatrixGateway>(s => s.Fault("GetMediaMetadataAsync", error));

        var result = await CreateService(gateway).GetMediaMetadataBatchAsync(["mxc://example.com/a\nb"], _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(("mxc://example.com/a\nb", (string?)null), (Assert.Single(result.Data!).MediaId, result.Data![0].MediaType));
        AssertSingleLog(LogLevel.Debug, "Failed to fetch metadata for media {Mxc}", new() { ["Mxc"] = "mxc://example.com/a b" }, error);
    }

    [Fact]
    public async Task MetadataBatch_ItemCancelled_ReturnsItUnhydrated_WithoutLogging()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.Fault("GetMediaMetadataAsync", new TaskCanceledException()));

        var result = await CreateService(gateway).GetMediaMetadataBatchAsync(["mxc://example.com/a"], _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Single(result.Data!);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task LargestRooms_NameLookupFails_KeepsTheRoomId_AndLogsAtDebug()
    {
        var error = new InvalidOperationException("boom");
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("GetLargestRoomsAsync", _ => Task.FromResult<RoomStatisticsResponse?>(new() { Rooms = [new() { RoomId = RoomId, EstimatedSize = 7 }] }))
            .Fault("GetRoomDetailsAsync", error));

        var result = await CreateService(gateway).GetLargestRoomsAsync(_cts.Token);

        ResultAssert.Success(result, "");
        var room = Assert.Single(result.Data!);
        Assert.Equal((RoomId, RoomId, 7L), (room.RoomId, room.Name, room.EstimatedSize));
        AssertSingleLog(LogLevel.Debug, "Failed to fetch metadata for room {RoomId} during statistics gathering.", RoomArgument, error);
    }

    [Fact]
    public async Task LargestRooms_NameLookupCancelled_KeepsTheRoomId_WithoutLogging()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("GetLargestRoomsAsync", _ => Task.FromResult<RoomStatisticsResponse?>(new() { Rooms = [new() { RoomId = RoomId }] }))
            .Fault("GetRoomDetailsAsync", new TaskCanceledException()));

        var result = await CreateService(gateway).GetLargestRoomsAsync(_cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(RoomId, Assert.Single(result.Data!).Name);
        Assert.Empty(_logger.Entries);
    }

    private RoomService CreateService(IMatrixGateway? gateway)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new RoomService(session, _logger, new KeyLocalizer(), _cache);
    }

    private void AssertSingleLog(LogLevel level, string template, Dictionary<string, object?> arguments, Exception? exception)
    {
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((level, template), (entry.Level, entry.Template));
        Assert.Equal(arguments, entry.Arguments);
        Assert.Same(exception, entry.Exception);
    }
}
