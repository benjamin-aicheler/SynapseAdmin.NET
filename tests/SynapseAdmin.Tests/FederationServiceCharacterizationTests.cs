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
/// Characterization of the result and logging contract of both <see cref="FederationService"/> methods (P3-11c):
/// no session, forwarded arguments, cancellation, gateway failure, null gateway result and success.
/// </summary>
public sealed class FederationServiceCharacterizationTests : IDisposable
{
    // The line break proves that the log calls sanitize the destination.
    private const string Destination = "remote\n.example";

    private sealed record Operation(
        string GatewayMethod,
        Func<FederationService, CancellationToken, Task<OperationResult>> Invoke,
        Func<CancellationToken, object?[]> GatewayArgs,
        string FailureKey,
        string ErrorTemplate,
        Dictionary<string, object?> ErrorArguments);

    private static readonly Dictionary<string, Operation> Operations = new()
    {
        ["List"] = new("GetFederationDestinationListAsync", async (s, ct) => await s.GetDestinationsAsync(20, 10, SortDirection.Ascending, ct),
            ct => [20, 10, "f", ct], "ErrorFetchingFederationDestinations",
            "Error fetching federation destinations (offset: {Offset}, limit: {Limit})",
            new() { ["Offset"] = 20, ["Limit"] = 10 }),
        ["Reset"] = new("ResetFederationConnectionTimeoutAsync", (s, ct) => s.ResetConnectionTimeoutAsync(Destination, ct),
            ct => [Destination, ct], "ErrorResettingFederationConnection", "Error resetting federation connection timeout for {Destination}",
            new() { ["Destination"] = "remote .example" }),
    };

    public static TheoryData<string> AllOperations => new(Operations.Keys);

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<FederationService> _logger = new();

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

        await CreateService(gateway).GetDestinationsAsync(0, 10, direction, _cts.Token);

        Assert.Equal(dir, Assert.Single(stub.CallsTo("GetFederationDestinationListAsync"))[2]);
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
        var result = await CreateService(Stub.Create<IMatrixGateway>()).GetDestinationsAsync(0, 10, SortDirection.None, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(0, result.Data.Total);
        Assert.Empty(result.Data.Destinations);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task List_Success_ReturnsTheTotalAndTheMappedDestinations()
    {
        var page = new SynapseAdminDestinationListResult { Total = 42, Destinations = [new() { Destination = "a.example" }, new() { Destination = "b.example" }] };
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("GetFederationDestinationListAsync", _ => Task.FromResult<SynapseAdminDestinationListResult?>(page)));

        var result = await CreateService(gateway).GetDestinationsAsync(0, 10, SortDirection.None, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(42, result.Data.Total);
        Assert.Equal(["a.example", "b.example"], result.Data.Destinations.Select(d => d.Destination));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Reset_Success_ReturnsLocalizedMessage_AndLogsInformation()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).ResetConnectionTimeoutAsync(Destination, _cts.Token);

        ResultAssert.Success(result, "ResetFederationConnectionSuccessful");
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Information, "Successfully reset federation connection timeout for {Destination}"), (entry.Level, entry.Template));
        Assert.Equal(new Dictionary<string, object?> { ["Destination"] = "remote .example" }, entry.Arguments);
    }

    private FederationService CreateService(IMatrixGateway? gateway)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new FederationService(session, _logger, new KeyLocalizer());
    }
}
