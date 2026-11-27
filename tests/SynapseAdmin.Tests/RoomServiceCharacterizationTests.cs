using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
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
    private const string DeleteId = "delete\n1";
    private const string Search = "ali\nce";
    private const string ServerName = "example.com";
    private const string AdminUserId = "@admin:example.com";
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
        ["Delete"] = new("DeleteRoomAsync", async (s, ct) => await s.DeleteRoomAsync(RoomId, block: true, purge: false, ct),
            (args, ct) =>
            {
                Assert.Equal(RoomId, args[0]);
                var request = Assert.IsType<SynapseAdminRoomDeleteRequest>(args[1]);
                Assert.Equal((true, false, (string?)null, (string?)null, (string?)null),
                    (request.Block, request.Purge, request.NewRoomUserId, request.RoomName, request.Message));
                Assert.Equal(ct, args[2]);
            },
            // Success depends on the delete_id the gateway returns: see the Delete_* tests (#518).
            "ErrorDeletingRoom", "Error deleting room {RoomId}", RoomArgument),
        ["DeletionStatus"] = new("GetRoomDeleteStatusAsync", async (s, ct) => await s.GetRoomDeletionStatusAsync(DeleteId, ct),
            Args(DeleteId), "ErrorFetchingRoomDeletionStatus", "Error getting room deletion status for delete ID {DeleteId}",
            new() { ["DeleteId"] = "delete 1" }),
        ["ActiveDeletion"] = new("GetRoomDeleteStatusesAsync", async (s, ct) => await s.GetActiveRoomDeletionAsync(RoomId, ct),
            Args(RoomId), "ErrorFetchingRoomDeletionStatus", "Error looking up deletions of room {RoomId}", RoomArgument),
        ["QuarantineMedia"] = new("QuarantineMediaByRoomIdAsync", (s, ct) => s.QuarantineMediaAsync(RoomId, ct),
            Args(RoomId), "ErrorQuarantiningRoomMedia", "Error quarantining media for room {RoomId}", RoomArgument,
            "RoomMediaQuarantinedSuccessfully", "Successfully quarantined media for room {RoomId}", RoomArgument),
        ["Block"] = new("BlockRoomAsync", (s, ct) => s.BlockRoomAsync(RoomId, true, ct),
            Args(RoomId, true), "ErrorBlockingRoom", "Error blocking/unblocking room {RoomId}", RoomArgument,
            "RoomBlockedSuccessfully", "Successfully {Action} room {RoomId}", new() { ["Action"] = "blocked", ["RoomId"] = SanitizedRoomId }),
        ["Unblock"] = new("BlockRoomAsync", (s, ct) => s.BlockRoomAsync(RoomId, false, ct),
            Args(RoomId, false), "ErrorUnblockingRoom", "Error blocking/unblocking room {RoomId}", RoomArgument,
            "RoomUnblockedSuccessfully", "Successfully {Action} room {RoomId}", new() { ["Action"] = "unblocked", ["RoomId"] = SanitizedRoomId }),
        ["BlockStatus"] = new("GetRoomBlockStatusAsync", async (s, ct) => await s.GetRoomBlockStatusAsync(RoomId, ct),
            Args(RoomId), "ErrorFetchingRoomBlockStatus", "Error fetching block status for room {RoomId}", RoomArgument),
        // Synapse doesn't check the server of the user it makes room admin (#587).
        ["MakeAdmin"] = new("MakeRoomAdminAsync", (s, ct) => s.MakeRoomAdminAsync(RoomId, " @bob:other.org ", ct),
            Args(RoomId, "@bob:other.org"), "ErrorMakingRoomAdmin", "Error making {UserId} room admin in room {RoomId}",
            new() { ["UserId"] = "@bob:other.org", ["RoomId"] = SanitizedRoomId },
            "RoomAdminGranted", "Made {UserId} room admin in room {RoomId}", new() { ["UserId"] = "@bob:other.org", ["RoomId"] = SanitizedRoomId }),
        // A blank user is the signed-in admin: no user_id is sent, and the success log names the session's user.
        ["MakeAdminSelf"] = new("MakeRoomAdminAsync", (s, ct) => s.MakeRoomAdminAsync(RoomId, "  ", ct),
            Args(RoomId, null), "ErrorMakingRoomAdmin", "Error making {UserId} room admin in room {RoomId}",
            new() { ["UserId"] = "the signed-in admin", ["RoomId"] = SanitizedRoomId },
            "RoomAdminGranted", "Made {UserId} room admin in room {RoomId}", new() { ["UserId"] = AdminUserId, ["RoomId"] = SanitizedRoomId }),
        // Success needs the room ID the gateway returns: see the Join_* tests (#587).
        ["Join"] = new("JoinUserToRoomAsync", async (s, ct) => await s.JoinUserToRoomAsync(RoomId, " @bob:example.com ", ct),
            Args(RoomId, "@bob:example.com"), "ErrorJoiningUserToRoom", "Error joining {UserId} to room {RoomId}",
            new() { ["UserId"] = "@bob:example.com", ["RoomId"] = SanitizedRoomId }),
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
        var gateway = TableGateway(out var stub);

        await operation.Invoke(CreateService(gateway), _cts.Token);

        // Property reads (the homeserver URL for cache keys, the server name and user ID for room recovery) aren't calls.
        var call = Assert.Single(stub.Calls, c => !c.Method.StartsWith("get_", StringComparison.Ordinal));
        Assert.Equal(operation.GatewayMethod, call.Method);
        operation.AssertGatewayArgs(call.Args, _cts.Token);
    }

    // The documented key goes out as is, never as the deprecated alias "alphabetical" (#545).
    [Fact]
    public async Task List_SendsTheNameKeyWithoutAnAlias()
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        await CreateService(gateway).GetRoomListAsync(0, 10, "name", SortDirection.None, token: _cts.Token);

        var args = Assert.Single(stub.CallsTo("GetRoomListAsync"));
        Assert.Equal(("name", "f"), ((string?)args[2], (string?)args[3]));
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayCancels_ReturnsSilentCancellation_WithoutLogging(string name)
    {
        var gateway = TableGateway(s => s.Fault(Operations[name].GatewayMethod, new TaskCanceledException()));

        var result = await Operations[name].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Cancelled(result);
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayThrows_FailsWithOperationKey_AndLogsError(string name)
    {
        var operation = Operations[name];
        var error = new InvalidOperationException("boom");
        var gateway = TableGateway(s => s.Fault(operation.GatewayMethod, error));

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, operation.FailureKey);
        AssertSingleLog(LogLevel.Error, operation.ErrorTemplate, operation.ErrorArguments, error);
    }

    // A read that a page runs by itself logs a refusal at Debug: the homeserver works as intended (#559). Every other
    // refused operation logs its error, as for any failure.
    private static readonly Dictionary<string, (string Template, Dictionary<string, object?> Arguments)> RefusalsLoggedAtDebug = new()
    {
        ["ActiveDeletion"] = ("The homeserver doesn't support looking up deletions of room {RoomId}", RoomArgument),
        ["BlockStatus"] = ("The homeserver doesn't support reading the block status of room {RoomId}", RoomArgument),
        ["LargestRooms"] = ("The homeserver doesn't support room statistics by database size", []),
    };

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayRefuses_ReturnsTheNotSupportedWarning_AndLogsAtTheOperationsLevel(string name)
    {
        var operation = Operations[name];
        var refusal = new OperationNotSupportedByHomeserverException(operation.GatewayMethod);
        var gateway = TableGateway(s => s.Fault(operation.GatewayMethod, refusal));

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, "OperationNotSupportedByHomeserver", Severity.Warning);
        var entry = Assert.Single(_logger.Entries);
        if (RefusalsLoggedAtDebug.TryGetValue(name, out var debug))
        {
            Assert.Equal((LogLevel.Debug, debug.Template), (entry.Level, entry.Template));
            Assert.Equal(debug.Arguments, entry.Arguments);
            Assert.Null(entry.Exception);
        }
        else
        {
            Assert.Equal((LogLevel.Error, operation.ErrorTemplate), (entry.Level, entry.Template));
            Assert.Equal(operation.ErrorArguments, entry.Arguments);
            Assert.Same(refusal, entry.Exception);
        }
    }

    [Theory, MemberData(nameof(ActionOperations))]
    public async Task Action_Success_ReturnsLocalizedMessage_AndLogsInformationIfAny(string name)
    {
        var operation = Operations[name];

        var result = await operation.Invoke(CreateService(TableGateway()), _cts.Token);

        ResultAssert.Success(result, operation.SuccessKey!);
        if (operation.InfoTemplate == null) Assert.Empty(_logger.Entries);
        else AssertSingleLog(LogLevel.Information, operation.InfoTemplate, operation.InfoArguments!, exception: null);
    }

    [Theory]
    [InlineData("Details", "RoomNotFound")]
    [InlineData("Purge", "ErrorPurgingHistory")]
    [InlineData("PurgeStatus", "ErrorFetchingPurgeStatus")]
    [InlineData("GetVisibility", "ErrorFetchingRoomDirectoryVisibility")]
    [InlineData("BlockStatus", "ErrorFetchingRoomBlockStatus")]
    [InlineData("Join", "ErrorJoiningUserToRoom")]
    public async Task SingleResource_NullGatewayResult_Fails_WithoutLogging(string name, string key)
    {
        var result = await Operations[name].Invoke(CreateService(TableGateway()), _cts.Token);

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

    // ---- Room deletion (#518) ------------------------------------------------------------------------

    private static readonly Dictionary<string, object?> DeleteRequestArguments =
        new() { ["RoomId"] = SanitizedRoomId, ["Block"] = true, ["Purge"] = false };

    private RoomService DeletingService(Func<Task<SynapseAdminRoomDeleteResponse?>> response) =>
        CreateService(Stub.Create<IMatrixGateway>(s => s.On("DeleteRoomAsync", _ => response())));

    [Fact]
    public async Task Delete_WithDeleteId_ReportsTheRequest_ReturnsTheId_AndLogsIt()
    {
        var service = DeletingService(() => Task.FromResult<SynapseAdminRoomDeleteResponse?>(new() { DeleteId = DeleteId }));

        var result = await service.DeleteRoomAsync(RoomId, block: true, purge: false, _cts.Token);

        ResultAssert.Success(result, "RoomDeletionRequested");
        Assert.Equal(DeleteId, result.Data);
        AssertSingleLog(LogLevel.Information, "Requested deletion of room {RoomId} (delete ID {DeleteId}, block: {Block}, purge: {Purge})",
            new() { ["RoomId"] = SanitizedRoomId, ["DeleteId"] = "delete 1", ["Block"] = true, ["Purge"] = false }, exception: null);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "  ")]
    public async Task Delete_WithoutDeleteId_ReportsTheRequest_WithAWarning(bool hasBody, string? deleteId)
    {
        var response = hasBody ? new SynapseAdminRoomDeleteResponse { DeleteId = deleteId } : null;
        var service = DeletingService(() => Task.FromResult(response));

        var result = await service.DeleteRoomAsync(RoomId, block: true, purge: false, _cts.Token);

        ResultAssert.Success(result, "RoomDeletionRequested");
        Assert.Equal("", result.Data);
        AssertSingleLog(LogLevel.Warning, "Deletion of room {RoomId} was requested, but the homeserver returned no delete_id (block: {Block}, purge: {Purge})",
            DeleteRequestArguments, exception: null);
    }

    /// <summary>The homeserver accepted the deletion (2xx); only its answer can't be read, so it isn't an error.</summary>
    [Fact]
    public async Task Delete_NonJsonResponse_ReportsTheRequest_ForgetsTheRoom_AndLogsAWarning()
    {
        var error = new JsonException("'A' is an invalid start of a value.");
        var service = CreateService(Stub.Create<IMatrixGateway>(s => s.Fault("DeleteRoomAsync", error)));
        service.SetActivePurgeId(RoomId, "purge");

        var result = await service.DeleteRoomAsync(RoomId, block: true, purge: false, _cts.Token);

        ResultAssert.Success(result, "RoomDeletionRequested");
        Assert.Equal("", result.Data);
        Assert.Null(service.GetActivePurgeId(RoomId));
        AssertSingleLog(LogLevel.Warning, "Deletion of room {RoomId} was requested, but the homeserver's response is not JSON (block: {Block}, purge: {Purge})",
            DeleteRequestArguments, error);
    }

    private RoomService DeletionStatusService(SynapseAdminRoomDeleteStatusResponse? status) =>
        CreateService(Stub.Create<IMatrixGateway>(s => s.On("GetRoomDeleteStatusAsync", _ => Task.FromResult(status))));

    [Theory]
    [InlineData("scheduled", RoomDeletionState.Scheduled)]
    [InlineData("active", RoomDeletionState.Active)]
    [InlineData("complete", RoomDeletionState.Complete)]
    [InlineData("paused", RoomDeletionState.Unknown)]
    public async Task DeletionStatus_ReturnsTheMappedStatus_WithoutLogging(string status, RoomDeletionState state)
    {
        var service = DeletionStatusService(new() { DeleteId = DeleteId, RoomId = RoomId, Status = status });

        var result = await service.GetRoomDeletionStatusAsync(DeleteId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal((DeleteId, RoomId, state), (result.Data!.DeleteId, result.Data.RoomId, result.Data.State));
        Assert.Empty(_logger.Entries);
    }

    /// <summary>
    /// Unlike other single-resource reads, a null result is an answer, not a failure: the homeserver no longer knows the
    /// deletion, and the room page must not count it toward its cap of failed checks.
    /// </summary>
    [Fact]
    public async Task DeletionStatus_NullGatewayResult_IsUnknown_WithoutLogging()
    {
        var result = await DeletionStatusService(null).GetRoomDeletionStatusAsync(DeleteId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal((DeleteId, RoomDeletionState.Unknown), (result.Data!.DeleteId, result.Data.State));
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData("disk\nfull", "disk full")]
    [InlineData(null, null)]
    public async Task DeletionStatus_Failed_LogsTheHomeserversError(string? error, string? loggedError)
    {
        var service = DeletionStatusService(new() { DeleteId = DeleteId, RoomId = RoomId, Status = "failed", Error = error });

        var result = await service.GetRoomDeletionStatusAsync(DeleteId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(RoomDeletionState.Failed, result.Data!.State);
        AssertSingleLog(LogLevel.Warning, "Room deletion {DeleteId} of room {RoomId} failed: {Error}",
            new() { ["DeleteId"] = "delete 1", ["RoomId"] = SanitizedRoomId, ["Error"] = loggedError }, exception: null);
    }

    /// <summary>Each entry is <c>deleteId:status</c>. Active wins over scheduled; finished ones and entries without an ID are skipped.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(null, "d1:complete", "d2:failed")]
    [InlineData("d2", "d1:complete", "d2:scheduled")]
    [InlineData("d4", "d1:complete", "d2:scheduled", "d3:failed", "d4:active")]
    [InlineData("d2", ":active", "d2:scheduled")]
    public async Task ActiveDeletion_PicksTheRunningDeletion_ActiveBeforeScheduled(string? expected, params string[] entries)
    {
        var statuses = new SynapseAdminRoomDeleteStatusListResponse
        {
            Results = entries.Select(e => e.Split(':')).Select(p => new SynapseAdminRoomDeleteStatusResponse { DeleteId = p[0], RoomId = RoomId, Status = p[1] }).ToList()
        };
        var service = CreateService(Stub.Create<IMatrixGateway>(s => s.On("GetRoomDeleteStatusesAsync", _ => Task.FromResult<SynapseAdminRoomDeleteStatusListResponse?>(statuses))));

        var result = await service.GetActiveRoomDeletionAsync(RoomId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(expected, result.Data?.DeleteId);
        Assert.Empty(_logger.Entries);
    }

    /// <summary>Collection read: no answer means no deletion the homeserver knows of (#395).</summary>
    [Fact]
    public async Task ActiveDeletion_NullGatewayResult_IsNone_WithoutLogging()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).GetActiveRoomDeletionAsync(RoomId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Null(result.Data);
        Assert.Empty(_logger.Entries);
    }

    /// <summary>The homeserver's error text for a failed purge goes only to the log; the page shows a localized message (#518).</summary>
    [Theory]
    [InlineData("disk\nfull", "disk full")]
    [InlineData(null, null)]
    public async Task PurgeStatus_Failed_LogsTheHomeserversError(string? error, string? loggedError)
    {
        var status = new SynapseAdminPurgeHistoryStatusResponse { Status = "failed", Error = error };
        var service = CreateService(Stub.Create<IMatrixGateway>(s => s.On("GetPurgeHistoryStatusAsync", _ => Task.FromResult<SynapseAdminPurgeHistoryStatusResponse?>(status))));

        var result = await service.GetPurgeHistoryStatusAsync(PurgeId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Same(status, result.Data);
        AssertSingleLog(LogLevel.Warning, "History purge {PurgeId} failed: {Error}",
            new() { ["PurgeId"] = "purge 1", ["Error"] = loggedError }, exception: null);
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
    [InlineData(true, "@admin:example.com")]
    [InlineData(false, null)]
    public async Task BlockStatus_Success_ReturnsTheMappedState_WithoutLogging(bool block, string? userId)
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("GetRoomBlockStatusAsync",
            _ => Task.FromResult<SynapseAdminRoomBlockStatusResponse?>(new() { Block = block, UserId = userId })));

        var result = await CreateService(gateway).GetRoomBlockStatusAsync(RoomId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal((block, userId ?? string.Empty), (result.Data!.IsBlocked, result.Data.BlockedBy));
        Assert.Empty(_logger.Entries);
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

    // ---- Room recovery (#587) ------------------------------------------------------------------------

    private static IReadOnlyList<string> GatewayCalls(Stub stub) =>
        stub.Calls.Where(c => !c.Method.StartsWith("get_", StringComparison.Ordinal)).Select(c => c.Method).ToList();

    /// <summary>A localizer that appends the format arguments to the key, to check what a message names.</summary>
    private static IStringLocalizer<SharedResources> ArgumentLocalizer() =>
        Stub.Create<IStringLocalizer<SharedResources>>(s => s.On("get_Item", args =>
        {
            var key = (string)args[0]!;
            var values = args.Length > 1 ? (object[])args[1]! : [];
            return new LocalizedString(key, values.Length == 0 ? key : $"{key}({string.Join(", ", values)})");
        }));

    [Theory]
    [InlineData("bob")]
    [InlineData("@bob")]
    [InlineData("@:example.com")]
    [InlineData("@bob:")]
    [InlineData("@bo b:example.com")]
    [InlineData("#room:example.com")]
    public async Task MakeAdmin_MalformedUserId_FailsWithAWarning_WithoutARequest(string userId)
    {
        var gateway = TableGateway(out var stub);

        var result = await CreateService(gateway).MakeRoomAdminAsync(RoomId, userId, _cts.Token);

        ResultAssert.Failure(result, "InvalidUserId", Severity.Warning);
        Assert.Empty(GatewayCalls(stub));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task MakeAdmin_Success_NamesTheUserAndTheRoom()
    {
        var result = await CreateService(TableGateway(), ArgumentLocalizer()).MakeRoomAdminAsync(RoomId, "@bob:other.org", _cts.Token);

        ResultAssert.Success(result, $"RoomAdminGranted(@bob:other.org, {RoomId})");
    }

    [Fact]
    public async Task MakeAdmin_SignedInAdmin_NamesTheSessionsUser()
    {
        var result = await CreateService(TableGateway(), ArgumentLocalizer()).MakeRoomAdminAsync(RoomId, null, _cts.Token);

        ResultAssert.Success(result, $"RoomAdminGranted({AdminUserId}, {RoomId})");
    }

    /// <summary>
    /// Synapse and Tuwunel compare the server name exactly, so a mixed-case server is refused here too, unlike
    /// <c>MatrixIdHelper.IsOnServer</c>.
    /// </summary>
    [Theory]
    [InlineData("", "InvalidUserId")]
    [InlineData("bob", "InvalidUserId")]
    [InlineData("@bob:", "InvalidUserId")]
    [InlineData("@bob:other.org", "JoinUserNotLocal")]
    [InlineData("@bob:Example.com", "JoinUserNotLocal")]
    [InlineData("@bob:example.com.evil", "JoinUserNotLocal")]
    [InlineData("@bob:example.com:8448", "JoinUserNotLocal")]
    public async Task Join_InvalidOrRemoteUser_FailsWithAWarning_WithoutARequest(string userId, string key)
    {
        var gateway = TableGateway(out var stub);

        var result = await CreateService(gateway).JoinUserToRoomAsync(RoomId, userId, _cts.Token);

        ResultAssert.Failure(result, key, Severity.Warning);
        Assert.Empty(GatewayCalls(stub));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Join_NotLocal_NamesTheServer()
    {
        var result = await CreateService(TableGateway(), ArgumentLocalizer()).JoinUserToRoomAsync(RoomId, "@bob:other.org", _cts.Token);

        ResultAssert.Failure(result, $"JoinUserNotLocal({ServerName})", Severity.Warning);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Join_UnknownServerName_RefusesEveryUser(string? serverName)
    {
        var gateway = TableGateway(out var stub, s => s.On("get_ServerName", _ => serverName));

        var result = await CreateService(gateway).JoinUserToRoomAsync(RoomId, "@bob:example.com", _cts.Token);

        ResultAssert.Failure(result, "JoinUserNotLocal", Severity.Warning);
        Assert.Empty(GatewayCalls(stub));
    }

    [Fact]
    public async Task Join_Success_NamesTheUserAndTheRoom_AndLogsInformation()
    {
        var gateway = TableGateway(s => s.On("JoinUserToRoomAsync",
            _ => Task.FromResult<SynapseAdminJoinRoomResponse?>(new() { RoomId = "!room:example.com" })));

        var result = await CreateService(gateway, ArgumentLocalizer()).JoinUserToRoomAsync(RoomId, "@bob:example.com", _cts.Token);

        ResultAssert.Success(result, $"UserJoinedRoom(@bob:example.com, {RoomId})");
        AssertSingleLog(LogLevel.Information, "Joined {UserId} to room {RoomId}",
            new() { ["UserId"] = "@bob:example.com", ["RoomId"] = SanitizedRoomId }, exception: null);
    }

    private RoomService CreateService(IMatrixGateway? gateway, IStringLocalizer<SharedResources>? localizer = null)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new RoomService(session, _logger, localizer ?? new KeyLocalizer(), _cache);
    }

    /// <summary>A gateway on <see cref="ServerName"/>, signed in as <see cref="AdminUserId"/>, which room recovery reads.</summary>
    private static IMatrixGateway TableGateway(Action<Stub>? configure = null) => TableGateway(out _, configure);

    private static IMatrixGateway TableGateway(out Stub stub, Action<Stub>? configure = null)
    {
        var gateway = Stub.Create<IMatrixGateway>(out stub);
        stub.On("get_ServerName", _ => ServerName).On("get_UserId", _ => AdminUserId);
        configure?.Invoke(stub);
        return gateway;
    }

    private void AssertSingleLog(LogLevel level, string template, Dictionary<string, object?> arguments, Exception? exception)
    {
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((level, template), (entry.Level, entry.Template));
        Assert.Equal(arguments, entry.Arguments);
        Assert.Same(exception, entry.Exception);
    }
}
