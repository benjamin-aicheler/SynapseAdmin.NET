using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// <see cref="UserService.DeleteAllMediaAsync"/> (#599, AC-24): the loop always deletes the first page, stops when a
/// request deletes nothing, at the request cap, on cancellation or on a failure, then reads the media list to learn
/// what remains; each outcome gets its own result, and partial ones name the number deleted.
/// </summary>
public sealed class UserMediaDeletionTests
{
    private const string UserId = "@alice:example.com";

    private readonly RecordingLogger<UserService> _logger = new();
    private readonly List<int> _progress = [];

    /// <summary>A gateway whose deletions return <paramref name="perRequest"/> in turn (then 0) and whose list shows <paramref name="remaining"/>.</summary>
    private static IMatrixGateway Gateway(out Stub stub, int[] perRequest, int remaining = 0)
    {
        var gateway = Stub.Create<IMatrixGateway>(out stub);
        var calls = 0;
        stub.On(nameof(IMatrixGateway.DeleteUserMediaAsync), _ =>
        {
            var count = calls < perRequest.Length ? perRequest[calls] : 0;
            calls++;
            return Task.FromResult<SynapseAdminDeleteMediaResponse?>(new() { Total = count, DeletedMedia = Enumerable.Range(0, count).Select(i => $"m{i}").ToList() });
        });
        stub.On(nameof(IMatrixGateway.GetUserMediaAsync), _ => Task.FromResult<SynapseAdminUserMediaResult?>(new() { Total = remaining }));
        return gateway;
    }

    private UserService Service(IMatrixGateway gateway) =>
        new(Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway)), _logger, new KeyLocalizer());

    private Task<OperationResult<UserMediaDeletionViewModel>> Run(IMatrixGateway gateway, CancellationToken token = default) =>
        Service(gateway).DeleteAllMediaAsync(UserId, new SynchronousProgress(_progress.Add), token);

    private static List<string> Methods(Stub stub) => stub.Calls.Select(c => c.Method).ToList();

    private static void AssertResult(OperationResult<UserMediaDeletionViewModel> result, string key, Severity severity, bool success,
        int deleted, long? remaining, UserMediaDeletionOutcome outcome)
    {
        Assert.Equal((key, severity, success), (result.Message, result.Severity, result.Success));
        Assert.Equal((deleted, remaining, outcome), (result.Data!.Deleted, result.Data.Remaining, result.Data.Outcome));
    }

    [Fact]
    public async Task DeletesUntilARequestDeletesNothing_AlwaysFromTheFirstPage_ThenChecksTheList()
    {
        var gateway = Gateway(out var stub, [100, 100, 37]);

        var result = await Run(gateway);

        AssertResult(result, "UserMediaAllDeleted", Severity.Success, true, 237, 0, UserMediaDeletionOutcome.AllDeleted);
        Assert.Equal([.. Enumerable.Repeat("DeleteUserMediaAsync", 4), "GetUserMediaAsync"], Methods(stub));
        Assert.All(stub.CallsTo("DeleteUserMediaAsync"), args => Assert.Equal((UserId, 100), ((string?)args[0], (int)args[1]!)));
        Assert.Equal(new object?[] { UserId, 0, 1, "created_ts", "b" }, stub.CallsTo("GetUserMediaAsync").Single()[..5]);
        Assert.Equal([100, 200, 237], _progress);
    }

    /// <summary>Files the homeserver can't remove stay listed: a request that deletes nothing doesn't mean none are left.</summary>
    [Fact]
    public async Task NothingMoreDeleted_ButMediaRemain_IsAWarningWithBothCounts()
    {
        var gateway = Gateway(out _, [100, 20], remaining: 3);

        var result = await Run(gateway);

        AssertResult(result, "UserMediaSomeRemain", Severity.Warning, false, 120, 3, UserMediaDeletionOutcome.SomeRemain);
    }

    [Fact]
    public async Task NoMedia_IsAllDeleted_WithZero()
    {
        var gateway = Gateway(out var stub, []);

        var result = await Run(gateway);

        AssertResult(result, "UserMediaAllDeleted", Severity.Success, true, 0, 0, UserMediaDeletionOutcome.AllDeleted);
        Assert.Equal(["DeleteUserMediaAsync", "GetUserMediaAsync"], Methods(stub));
        Assert.Empty(_progress);
    }

    [Fact]
    public async Task NullAnswer_CountsAsNothingDeleted()
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        var result = await Run(gateway);

        AssertResult(result, "UserMediaAllDeleted", Severity.Success, true, 0, 0, UserMediaDeletionOutcome.AllDeleted);
        Assert.Equal(["DeleteUserMediaAsync", "GetUserMediaAsync"], Methods(stub));
    }

    [Fact]
    public async Task StopsAtTheRequestCap_AndReportsWhatIsLeft()
    {
        var gateway = Gateway(out var stub, Enumerable.Repeat(100, 150).ToArray(), remaining: 5000);

        var result = await Run(gateway);

        AssertResult(result, "UserMediaCapReached", Severity.Warning, false, 10_000, 5000, UserMediaDeletionOutcome.CapReached);
        Assert.Equal(UserService.MaxMediaDeletionRequests, stub.CallsTo("DeleteUserMediaAsync").Count);
    }

    [Fact]
    public async Task TheCapsLastRequest_EmptyingTheList_IsAllDeleted()
    {
        var gateway = Gateway(out _, Enumerable.Repeat(100, 100).ToArray());

        var result = await Run(gateway);

        AssertResult(result, "UserMediaAllDeleted", Severity.Success, true, 10_000, 0, UserMediaDeletionOutcome.AllDeleted);
    }

    /// <summary>A last request of the cap that deletes nothing is "some remain", not "cap reached".</summary>
    [Fact]
    public async Task TheCapsLastRequest_DeletingNothing_IsSomeRemain()
    {
        var gateway = Gateway(out _, [.. Enumerable.Repeat(100, 99), 0], remaining: 7);

        var result = await Run(gateway);

        AssertResult(result, "UserMediaSomeRemain", Severity.Warning, false, 9900, 7, UserMediaDeletionOutcome.SomeRemain);
    }

    [Fact]
    public async Task CancelledAfterSomeDeletions_ReportsThem_WithoutTheListRead()
    {
        using var cts = new CancellationTokenSource();
        var gateway = Gateway(out var stub, [100, 100, 100]);
        var service = Service(gateway);

        var result = await service.DeleteAllMediaAsync(UserId, new SynchronousProgress(n =>
        {
            _progress.Add(n);
            if (n == 200) cts.Cancel();
        }), cts.Token);

        AssertResult(result, "UserMediaDeletionStopped", Severity.Warning, false, 200, null, UserMediaDeletionOutcome.Stopped);
        Assert.Equal(["DeleteUserMediaAsync", "DeleteUserMediaAsync"], Methods(stub));
    }

    [Fact]
    public async Task CancelledBeforeAnyDeletion_IsASilentCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var gateway = Gateway(out var stub, [100]);

        var result = await Run(gateway, cts.Token);

        ResultAssert.Cancelled(result);
        Assert.Empty(stub.Calls);
        Assert.Empty(_logger.Entries);
    }

    /// <summary>A failed request isn't repeated, and the error names what was deleted before it.</summary>
    [Fact]
    public async Task FailureAfterSomeDeletions_IsAnErrorWithTheCount_AndNotRetried()
    {
        var error = new HttpRequestException("boom");
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        var calls = 0;
        stub.On(nameof(IMatrixGateway.DeleteUserMediaAsync), _ => ++calls == 1
            ? Task.FromResult<SynapseAdminDeleteMediaResponse?>(new() { Total = 100 })
            : Task.FromException<SynapseAdminDeleteMediaResponse?>(error));

        var result = await Run(gateway);

        AssertResult(result, "UserMediaDeletionFailed", Severity.Error, false, 100, null, UserMediaDeletionOutcome.Failed);
        Assert.Equal(["DeleteUserMediaAsync", "DeleteUserMediaAsync"], Methods(stub));
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Error, "Error deleting the media of user {UserId} after {Deleted} uploads in {Requests} requests"), (entry.Level, entry.Template));
        Assert.Same(error, entry.Exception);
    }

    [Fact]
    public async Task FailedListRead_ReportsTheDeletedCount_AndThatTheRestIsUnknown()
    {
        var gateway = Gateway(out var stub, [100, 5]);
        var error = new HttpRequestException("boom");
        stub.Fault(nameof(IMatrixGateway.GetUserMediaAsync), error);

        var result = await Run(gateway);

        AssertResult(result, "UserMediaRemainingUnknown", Severity.Warning, false, 105, null, UserMediaDeletionOutcome.RemainingUnknown);
        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Same(error, warning.Exception);
    }

    [Fact]
    public async Task Success_LogsCountsOnly_NoMediaIds()
    {
        var gateway = Gateway(out _, [2]);

        await Run(gateway);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(new Dictionary<string, object?>
        {
            ["Deleted"] = 2, ["UserId"] = UserId, ["Requests"] = 2, ["Outcome"] = UserMediaDeletionOutcome.AllDeleted, ["Remaining"] = 0L
        }, entry.Arguments);
    }

    /// <summary><see cref="Progress{T}"/> posts to a synchronization context; this reports at once, in order.</summary>
    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
