using Microsoft.Extensions.Logging;
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
/// no session, forwarded arguments, cancellation, gateway failure, null gateway result and success. The list follows
/// <c>next_token</c> until the homeserver leaves it out and fails as a whole when any page fails (#551).
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
        ["List"] = new("GetFederationDestinationListAsync", async (s, ct) => await s.GetAllDestinationsAsync(ct),
            ct => [null, 10000, "f", ct], "ErrorFetchingFederationDestinations",
            "Error fetching federation destinations (from: {From})",
            new() { ["From"] = null }),
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
    public async Task List_NullGatewayResult_IsAnEmptyList()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).GetAllDestinationsAsync(_cts.Token);

        ResultAssert.Success(result, "");
        Assert.Empty(result.Data!);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task List_SinglePageWithoutNextToken_ReturnsTheMappedDestinations()
    {
        var gateway = PagedGateway(out var stub, Page(null, "a.example", "b.example"));

        var result = await CreateService(gateway).GetAllDestinationsAsync(_cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(["a.example", "b.example"], result.Data!.Select(d => d.Destination));
        Assert.Single(stub.CallsTo("GetFederationDestinationListAsync"));
        Assert.Empty(_logger.Entries);
    }

    /// <summary>
    /// Each later request sends the previous <c>next_token</c> unchanged, also a cursor that isn't an offset, and
    /// the list ends only when the token is missing: a misleading <c>total</c> and an empty page don't end it.
    /// </summary>
    [Fact]
    public async Task List_FollowsNextTokenUntilItIsMissing_IgnoringTotal()
    {
        var gateway = PagedGateway(out var stub,
            Page("cursor-1", total: 1, "a.example"),
            Page("cursor-2", total: 0),
            Page(null, total: 99, "b.example", "c.example"));

        var result = await CreateService(gateway).GetAllDestinationsAsync(_cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(["a.example", "b.example", "c.example"], result.Data!.Select(d => d.Destination));
        Assert.Equal(new object?[] { null, "cursor-1", "cursor-2" }, stub.CallsTo("GetFederationDestinationListAsync").Select(args => args[0]));
        Assert.All(stub.CallsTo("GetFederationDestinationListAsync"), args => Assert.Equal(new object?[] { 10000, "f", _cts.Token }, args[1..]));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task List_NullLaterPage_EndsTheList()
    {
        var gateway = PagedGateway(out _, Page("cursor-1", "a.example"), null);

        var result = await CreateService(gateway).GetAllDestinationsAsync(_cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(["a.example"], result.Data!.Select(d => d.Destination));
    }

    /// <summary>A homeserver that returns a token it already returned would make the loop request pages forever.</summary>
    [Fact]
    public async Task List_RepeatedNextToken_FailsTheWholeLoad_AndLogsAWarning()
    {
        var gateway = PagedGateway(out var stub, Page("cursor\n1", "a.example"), Page("cursor-2", "b.example"), Page("cursor\n1", "c.example"));

        var result = await CreateService(gateway).GetAllDestinationsAsync(_cts.Token);

        ResultAssert.Failure(result, "ErrorFetchingFederationDestinations");
        Assert.Equal(3, stub.CallsTo("GetFederationDestinationListAsync").Count());
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Warning, "The homeserver repeated the federation destinations next_token {NextToken}"), (entry.Level, entry.Template));
        Assert.Equal(new Dictionary<string, object?> { ["NextToken"] = "cursor 1" }, entry.Arguments);
    }

    /// <summary>A later page that fails discards the pages already read, and the log names the token it was requested with.</summary>
    [Fact]
    public async Task List_LaterPageThrows_FailsTheWholeLoad_AndLogsItsToken()
    {
        var error = new InvalidOperationException("boom");
        var calls = 0;
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("GetFederationDestinationListAsync", _ => ++calls == 1
            ? Task.FromResult<SynapseAdminDestinationListResult?>(Page("cursor\n1", "a.example"))
            : Task.FromException<SynapseAdminDestinationListResult?>(error)));

        var result = await CreateService(gateway).GetAllDestinationsAsync(_cts.Token);

        ResultAssert.Failure(result, "ErrorFetchingFederationDestinations");
        Assert.Null(result.Data);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Error, "Error fetching federation destinations (from: {From})"), (entry.Level, entry.Template));
        Assert.Equal(new Dictionary<string, object?> { ["From"] = "cursor 1" }, entry.Arguments);
        Assert.Same(error, entry.Exception);
    }

    [Fact]
    public async Task List_CancelledBetweenPages_ReturnsSilentCancellation_WithoutAnotherRequest()
    {
        var calls = 0;
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("GetFederationDestinationListAsync", _ =>
        {
            calls++;
            _cts.Cancel();
            return Task.FromResult<SynapseAdminDestinationListResult?>(Page("cursor-1", "a.example"));
        }));

        var result = await CreateService(gateway).GetAllDestinationsAsync(_cts.Token);

        ResultAssert.Cancelled(result);
        Assert.Equal(1, calls);
        Assert.Empty(_logger.Entries);
    }

    private static SynapseAdminDestinationListResult Page(string? nextToken, params string[] destinations) =>
        Page(nextToken, destinations.Length, destinations);

    private static SynapseAdminDestinationListResult Page(string? nextToken, int total, params string[] destinations) => new()
    {
        Total = total,
        NextToken = nextToken,
        Destinations = destinations.Select(d => new SynapseAdminDestinationListResult.SynapseAdminDestinationListResultDestination { Destination = d }).ToList(),
    };

    /// <summary>A gateway that answers the list requests with these pages in order.</summary>
    private static IMatrixGateway PagedGateway(out Stub stub, params SynapseAdminDestinationListResult?[] pages)
    {
        var next = 0;
        var gateway = Stub.Create<IMatrixGateway>(out stub);
        stub.On("GetFederationDestinationListAsync", _ => Task.FromResult(pages[next++]));
        return gateway;
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
