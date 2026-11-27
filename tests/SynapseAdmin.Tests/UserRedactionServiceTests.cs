using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// What <see cref="UserRedactionService"/> sends, refuses, tracks and logs (#600). The result contract shared by every
/// method is in <see cref="UserRedactionServiceCharacterizationTests"/>.
/// </summary>
public sealed class UserRedactionServiceTests : IDisposable
{
    private const string UserId = "@spammer:example.com";
    private const string Homeserver = "https://hs.example";
    private const string Reason = "Spam from a stolen account";

    private readonly RecordingLogger<UserRedactionService> _logger = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    // ---- Starting ----------------------------------------------------------------------------------------

    /// <summary>An empty list would redact in the user's rooms, which the admin didn't choose.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Start_SelectedRoomsWithoutRooms_FailsWithAWarning_WithoutARequest(int blankRoomIds)
    {
        var roomIds = Enumerable.Repeat("  ", blankRoomIds).ToList();
        var gateway = Gateway(out var stub);

        var result = await CreateService(gateway).StartRedactionAsync(UserId, new() { AllRooms = false, RoomIds = roomIds, Limit = 0 });

        ResultAssert.Failure(result, "UserRedactionNoRoomsSelected", Severity.Warning);
        Assert.Empty(GatewayCalls(stub));
        Assert.Empty(_logger.Entries);
    }

    /// <summary>Synapse would take 0 as its default of 1000 and refuse a negative limit.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Start_LimitBelowOne_FailsWithAWarning_WithoutARequest(int limit)
    {
        var gateway = Gateway(out var stub);

        var result = await CreateService(gateway).StartRedactionAsync(UserId, new() { Limit = limit });

        ResultAssert.Failure(result, "UserRedactionInvalidLimit", Severity.Warning);
        Assert.Empty(GatewayCalls(stub));
    }

    [Fact]
    public async Task Start_UserRooms_SendsAnEmptyRoomList_EvenWithRoomsLeftSelected()
    {
        var gateway = Gateway(out var stub, redactId: "r1");

        await CreateService(gateway).StartRedactionAsync(UserId,
            new() { AllRooms = true, RoomIds = ["!a:example.com"], Reason = Reason, Limit = 50, UseAdmin = true });

        var request = SentRequest(stub);
        Assert.Empty(request.Rooms);
        Assert.Equal((Reason, 50, true), (request.Reason, request.Limit, request.UseAdmin));
    }

    [Fact]
    public async Task Start_SelectedRooms_SendsEachRoomOnce_InOrder()
    {
        var gateway = Gateway(out var stub, redactId: "r1");

        await CreateService(gateway).StartRedactionAsync(UserId,
            new() { AllRooms = false, RoomIds = ["!b:example.com", "!a:example.com", "!b:example.com", " ", "!B:example.com"] });

        var request = SentRequest(stub);
        Assert.Equal(["!b:example.com", "!a:example.com", "!B:example.com"], request.Rooms);
        Assert.Equal((UserRedactionRequestViewModel.DefaultLimit, false), (request.Limit, request.UseAdmin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_BlankReason_IsNotSent(string? reason)
    {
        var gateway = Gateway(out var stub, redactId: "r1");

        await CreateService(gateway).StartRedactionAsync(UserId, new() { Reason = reason });

        Assert.Null(SentRequest(stub).Reason);
    }

    [Fact]
    public async Task Start_Success_ReturnsTheId_TracksIt_AndLogsWithoutTheReason()
    {
        var service = CreateService(Gateway(out _, redactId: "r\n1"));

        var result = await service.StartRedactionAsync(UserId, new() { AllRooms = false, RoomIds = ["!a:example.com", "!b:example.com"], Reason = Reason, Limit = 20, UseAdmin = true });

        ResultAssert.Success(result, "UserRedactionStarted");
        Assert.Equal("r\n1", result.Data);
        Assert.Equal("r\n1", service.GetActiveRedactionId(UserId));
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Information, "Started redaction {RedactId} of events of user {UserId} in {Scope}, limit {Limit}, as admin {UseAdmin}"),
            (entry.Level, entry.Template));
        Assert.Equal(new Dictionary<string, object?>
        {
            ["RedactId"] = "r 1", ["UserId"] = UserId, ["Scope"] = "2 selected rooms", ["Limit"] = 20, ["UseAdmin"] = true
        }, entry.Arguments);
        AssertNeverLogged(Reason);
    }

    [Fact]
    public async Task Start_UserRooms_LogsTheScope()
    {
        await CreateService(Gateway(out _, redactId: "r1")).StartRedactionAsync(UserId, new() { Reason = Reason });

        Assert.Equal("the user's rooms", Assert.Single(_logger.Entries).Arguments["Scope"]);
        AssertNeverLogged(Reason);
    }

    /// <summary>The redaction runs, but without an ID its status can't be read, so nothing is tracked.</summary>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "")]
    public async Task Start_WithoutARedactId_SucceedsUntracked_AndLogsAWarning(bool answered, string? redactId)
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_HomeserverUrl", _ => Homeserver)
            .On("RedactUserEventsAsync", _ => Task.FromResult(answered ? new SynapseAdminUserRedactResponse { RedactId = redactId } : null)));
        var service = CreateService(gateway);

        var result = await service.StartRedactionAsync(UserId, new() { Reason = Reason });

        ResultAssert.Success(result, "UserRedactionStarted");
        Assert.Equal("", result.Data);
        Assert.Null(service.GetActiveRedactionId(UserId));
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Warning, "Redaction of events of user {UserId} started without returning a redact_id"), (entry.Level, entry.Template));
        AssertNeverLogged(Reason);
    }

    // ---- Tracking ----------------------------------------------------------------------------------------

    /// <summary>Every session shares the cache, and sessions can be on different homeservers (#431).</summary>
    [Theory]
    [InlineData(Homeserver, UserId, true)]
    [InlineData("https://HS.example/", UserId, true)]
    [InlineData("https://other.example", UserId, false)]
    [InlineData(Homeserver, "@other:example.com", false)]
    public async Task Tracking_IsPerHomeserverAndUser(string otherHomeserver, string otherUser, bool seen)
    {
        await CreateService(Gateway(out _, redactId: "r1")).StartRedactionAsync(UserId, new());

        var other = CreateService(Stub.Create<IMatrixGateway>(s => s.On("get_HomeserverUrl", _ => otherHomeserver)));

        Assert.Equal(seen ? "r1" : null, other.GetActiveRedactionId(otherUser));
    }

    [Fact]
    public async Task Tracking_ANewerRedaction_ReplacesTheOlderOne()
    {
        await CreateService(Gateway(out _, redactId: "r1")).StartRedactionAsync(UserId, new());
        var service = CreateService(Gateway(out _, redactId: "r2"));

        await service.StartRedactionAsync(UserId, new());

        Assert.Equal("r2", service.GetActiveRedactionId(UserId));
    }

    [Fact]
    public async Task Clear_TheTrackedRedaction_RemovesIt()
    {
        var service = CreateService(Gateway(out _, redactId: "r1"));
        await service.StartRedactionAsync(UserId, new());

        service.ClearActiveRedactionId(UserId, "r1");

        Assert.Null(service.GetActiveRedactionId(UserId));
    }

    /// <summary>A page that gives up on an older redaction must not drop a newer one another session started (#522).</summary>
    [Fact]
    public async Task Clear_AnOlderRedaction_KeepsTheNewerOne()
    {
        var service = CreateService(Gateway(out _, redactId: "r2"));
        await service.StartRedactionAsync(UserId, new());

        service.ClearActiveRedactionId(UserId, "r1");

        Assert.Equal("r2", service.GetActiveRedactionId(UserId));
    }

    [Fact]
    public async Task Tracking_WithoutASession_IsEmpty_AndClearDoesNothing()
    {
        var tracked = CreateService(Gateway(out _, redactId: "r1"));
        await tracked.StartRedactionAsync(UserId, new());
        var signedOut = CreateService(gateway: null);

        signedOut.ClearActiveRedactionId(UserId, "r1");

        Assert.Null(signedOut.GetActiveRedactionId(UserId));
        Assert.Equal("r1", tracked.GetActiveRedactionId(UserId));
    }

    // ---- Status ------------------------------------------------------------------------------------------

    /// <summary>The homeserver no longer knows the redaction: an answer, not a failed check.</summary>
    [Fact]
    public async Task Status_NotFound_IsUnknown_WithoutLogging()
    {
        var result = await CreateService(StatusGateway(null)).GetRedactionStatusAsync("r1");

        ResultAssert.Success(result, "");
        Assert.Equal(("r1", UserRedactionState.Unknown, 0), (result.Data!.RedactId, result.Data.State, result.Data.FailedRedactionCount));
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData("scheduled", UserRedactionState.Scheduled)]
    [InlineData("active", UserRedactionState.Active)]
    [InlineData("complete", UserRedactionState.Complete)]
    public async Task Status_WithoutFailures_IsMapped_WithoutLogging(string status, UserRedactionState state)
    {
        var result = await CreateService(StatusGateway(new() { Status = status, FailedRedactions = [] })).GetRedactionStatusAsync("r1");

        ResultAssert.Success(result, "");
        Assert.Equal(("r1", state), (result.Data!.RedactId, result.Data.State));
        Assert.Empty(_logger.Entries);
    }

    /// <summary>The homeserver's error text goes only to the log, sanitized; the page shows a localized message.</summary>
    [Fact]
    public async Task Status_Failed_LogsTheSanitizedError_ButDoesNotReturnIt()
    {
        var result = await CreateService(StatusGateway(new() { Status = "failed", Error = "database\nlocked" })).GetRedactionStatusAsync("r\n1");

        ResultAssert.Success(result, "");
        Assert.Equal(UserRedactionState.Failed, result.Data!.State);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Warning, "Redaction {RedactId} failed: {Error}"), (entry.Level, entry.Template));
        Assert.Equal(new Dictionary<string, object?> { ["RedactId"] = "r 1", ["Error"] = "database locked" }, entry.Arguments);
    }

    /// <summary>
    /// The count is always logged; the event IDs and errors only for the first
    /// <see cref="UserRedactionService.MaxLoggedFailedRedactions"/>, each sanitized.
    /// </summary>
    [Fact]
    public async Task Status_CompleteWithFailures_LogsTheCount_AndTheFirstFailures()
    {
        var failures = Enumerable.Range(1, UserRedactionService.MaxLoggedFailedRedactions + 5)
            .ToDictionary(i => $"$event{i}:example.com", i => $"error\n{i}");

        var result = await CreateService(StatusGateway(new() { Status = "complete", FailedRedactions = failures })).GetRedactionStatusAsync("r1");

        ResultAssert.Success(result, "");
        Assert.Equal((UserRedactionState.Complete, 25), (result.Data!.State, result.Data.FailedRedactionCount));
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Warning, "Redaction {RedactId} completed, but {FailedCount} events couldn't be redacted: {FailedRedactions}"),
            (entry.Level, entry.Template));
        Assert.Equal(("r1", 25), (entry.Arguments["RedactId"], entry.Arguments["FailedCount"]));
        var listed = Assert.IsType<string>(entry.Arguments["FailedRedactions"]).Split("; ");
        Assert.Equal(Enumerable.Range(1, 20).Select(i => $"$event{i}:example.com: error {i}"), listed);
    }

    private static IMatrixGateway Gateway(out Stub stub, string? redactId = null)
    {
        var gateway = Stub.Create<IMatrixGateway>(out stub);
        stub.On("get_HomeserverUrl", _ => Homeserver)
            .On("RedactUserEventsAsync", _ => Task.FromResult<SynapseAdminUserRedactResponse?>(new() { RedactId = redactId }));
        return gateway;
    }

    private static IMatrixGateway StatusGateway(SynapseAdminUserRedactStatusResponse? status) => Stub.Create<IMatrixGateway>(s => s
        .On("get_HomeserverUrl", _ => Homeserver)
        .On("GetUserRedactionStatusAsync", _ => Task.FromResult(status)));

    private static List<string> GatewayCalls(Stub stub) => stub.Calls.Select(c => c.Method).Where(m => !m.StartsWith("get_", StringComparison.Ordinal)).ToList();

    private static SynapseAdminUserRedactRequest SentRequest(Stub stub) =>
        Assert.IsType<SynapseAdminUserRedactRequest>(Assert.Single(stub.CallsTo("RedactUserEventsAsync"))[1]);

    private void AssertNeverLogged(string text) =>
        Assert.DoesNotContain(_logger.Entries, e => e.Template?.Contains(text) == true
            || e.Arguments.Values.Any(v => v?.ToString()?.Contains(text) == true));

    private UserRedactionService CreateService(IMatrixGateway? gateway) =>
        new(Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway)), _logger, new KeyLocalizer(), _cache);
}
