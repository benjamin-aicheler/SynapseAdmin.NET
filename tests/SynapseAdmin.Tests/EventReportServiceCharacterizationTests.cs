using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the result and logging contract of both <see cref="EventReportService"/> methods (P3-11c):
/// no session, forwarded arguments, cancellation, gateway failure, null gateway result and success.
/// </summary>
public sealed class EventReportServiceCharacterizationTests : IDisposable
{
    // The line breaks prove that the log calls sanitize the untrusted strings.
    private const string Search = "spam\nham";
    private const string ReportId = "report\n7";

    private sealed record Operation(
        string GatewayMethod,
        Func<EventReportService, CancellationToken, Task<OperationResult>> Invoke,
        Func<CancellationToken, object?[]> GatewayArgs,
        string FailureKey,
        string ErrorTemplate,
        Dictionary<string, object?> ErrorArguments);

    private static readonly Dictionary<string, Operation> Operations = new()
    {
        ["List"] = new("GetEventReportListAsync", async (s, ct) => await s.GetEventReportsAsync(20, 10, SortDirection.Ascending, Search, ct),
            ct => [20, 10, "f", Search, ct], "ErrorFetchingEventReports",
            "Error fetching event reports (offset: {Offset}, limit: {Limit}, searchTerm: {SearchTerm})",
            new() { ["Offset"] = 20, ["Limit"] = 10, ["SearchTerm"] = "spam ham" }),
        ["Delete"] = new("DeleteEventReportAsync", (s, ct) => s.DeleteEventReportAsync(ReportId, ct),
            ct => [ReportId, ct], "ErrorDeletingEventReport", "Error deleting event report {ReportId}",
            new() { ["ReportId"] = "report 7" }),
    };

    public static TheoryData<string> AllOperations => new(Operations.Keys);

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<EventReportService> _logger = new();

    public void Dispose() => _cts.Dispose();

    [Theory, MemberData(nameof(AllOperations))]
    public async Task NoSession_FailsNotAuthenticated_WithoutLogging(string name)
    {
        var result = await Operations[name].Invoke(CreateService(gateway: null), _cts.Token);

        ResultAssert.Failure(result, "NotAuthenticated");
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task ForwardsArgumentsToGateway(string name)
    {
        var operation = Operations[name];
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        await operation.Invoke(CreateService(gateway), _cts.Token);

        var call = Assert.Single(stub.Calls);
        Assert.Equal(operation.GatewayMethod, call.Method);
        Assert.Equal(operation.GatewayArgs(_cts.Token), call.Args);
    }

    [Theory]
    [InlineData(SortDirection.Ascending, "f")]
    [InlineData(SortDirection.Descending, "b")]
    [InlineData(SortDirection.None, "b")]
    public async Task List_MapsTheSortDirection_NewestFirstWhenUnsorted(SortDirection direction, string dir)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        await CreateService(gateway).GetEventReportsAsync(0, 10, direction, token: _cts.Token);

        Assert.Equal(dir, Assert.Single(stub.CallsTo("GetEventReportListAsync"))[2]);
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
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Error, operation.ErrorTemplate), (entry.Level, entry.Template));
        Assert.Equal(operation.ErrorArguments, entry.Arguments);
        Assert.Same(error, entry.Exception);
    }

    [Fact]
    public async Task List_NullGatewayResult_IsAnEmptyPage()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).GetEventReportsAsync(0, 10, SortDirection.None, token: _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(0, result.Data.Total);
        Assert.Empty(result.Data.Reports);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task List_Success_ReturnsTheTotalAndTheMappedReports()
    {
        var page = new SynapseAdminEventReportListResult
        {
            Total = 42,
            Reports = [new() { Id = "1", EventId = "$a", RoomId = "!r" }, new() { Id = "2", EventId = "$b", RoomId = "!r" }],
        };
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("GetEventReportListAsync", _ => Task.FromResult<SynapseAdminEventReportListResult?>(page)));

        var result = await CreateService(gateway).GetEventReportsAsync(0, 10, SortDirection.None, token: _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(42, result.Data.Total);
        Assert.Equal(["1", "2"], result.Data.Reports.Select(r => r.Id));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Delete_Success_ReturnsLocalizedMessage_AndLogsInformation()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).DeleteEventReportAsync(ReportId, _cts.Token);

        ResultAssert.Success(result, "EventReportDeletedSuccessfully");
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Information, "Successfully deleted event report {ReportId}"), (entry.Level, entry.Template));
        Assert.Equal(new Dictionary<string, object?> { ["ReportId"] = "report 7" }, entry.Arguments);
    }

    private EventReportService CreateService(IMatrixGateway? gateway)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new EventReportService(session, _logger, new KeyLocalizer());
    }
}
