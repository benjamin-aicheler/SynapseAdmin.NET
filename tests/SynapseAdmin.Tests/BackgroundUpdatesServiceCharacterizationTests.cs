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
/// Characterization of the result and logging contract of every <see cref="BackgroundUpdatesService"/> method
/// (P3-11d): no session, forwarded arguments, cancellation, gateway failure, null gateway result and success.
/// </summary>
public sealed class BackgroundUpdatesServiceCharacterizationTests : IDisposable
{
    // The line break proves that the log call sanitizes the job name.
    private const string JobName = "populate\nstats";

    private sealed record Operation(
        string GatewayMethod,
        Func<BackgroundUpdatesService, CancellationToken, Task<OperationResult>> Invoke,
        Func<CancellationToken, object?[]> GatewayArgs,
        string FailureKey,
        string ErrorTemplate,
        Dictionary<string, object?> ErrorArguments);

    private static readonly Dictionary<string, Operation> Operations = new()
    {
        ["Status"] = new("GetBackgroundUpdatesStatusAsync", async (s, ct) => await s.GetStatusAsync(ct),
            ct => [ct], "ErrorFetchingBackgroundUpdatesStatus", "Error fetching background updates status", []),
        ["SetEnabled"] = new("SetBackgroundUpdatesEnabledAsync", async (s, ct) => await s.SetEnabledAsync(true, ct),
            ct => [true, ct], "ErrorTogglingBackgroundUpdates", "Error toggling background updates enabled state to {Enabled}",
            new() { ["Enabled"] = true }),
        ["StartJob"] = new("StartBackgroundUpdatesJobAsync", (s, ct) => s.StartJobAsync(JobName, ct),
            ct => [JobName, ct], "ErrorStartingBackgroundUpdateJob", "Error starting background updates job {JobName}",
            new() { ["JobName"] = "populate stats" }),
    };

    public static TheoryData<string> AllOperations => new(Operations.Keys);

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<BackgroundUpdatesService> _logger = new();

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

    [Theory]
    [InlineData("Status")]
    [InlineData("SetEnabled")]
    public async Task NullGatewayResult_FailsWithOperationKey_WithoutLogging(string name)
    {
        var result = await Operations[name].Invoke(CreateService(Stub.Create<IMatrixGateway>()), _cts.Token);

        ResultAssert.Failure(result, Operations[name].FailureKey);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Status_Success_MapsTheStatus_WithoutLogging()
    {
        var status = new SynapseAdminBackgroundUpdatesStatusResponse
        {
            Enabled = true,
            CurrentUpdates = new() { ["main"] = new() { Name = "populate_stats", TotalItemCount = 5 } },
        };
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("GetBackgroundUpdatesStatusAsync", _ => Task.FromResult<SynapseAdminBackgroundUpdatesStatusResponse?>(status)));

        var result = await CreateService(gateway).GetStatusAsync(_cts.Token);

        ResultAssert.Success(result, "");
        Assert.True(result.Data!.Enabled);
        Assert.Equal(("populate_stats", 5L), (result.Data.CurrentUpdates["main"].Name, result.Data.CurrentUpdates["main"].TotalItemCount));
        Assert.Empty(_logger.Entries);
    }

    // The data and the message describe the state the server reports, not the requested one.
    [Theory]
    [InlineData(true, "BackgroundUpdatesStatus: Active")]
    [InlineData(false, "BackgroundUpdatesStatus: Paused")]
    public async Task SetEnabled_Success_ReportsTheServersState_WithoutLogging(bool enabled, string message)
    {
        var response = new SynapseAdminBackgroundUpdatesEnabledResponse { Enabled = enabled };
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("SetBackgroundUpdatesEnabledAsync", _ => Task.FromResult<SynapseAdminBackgroundUpdatesEnabledResponse?>(response)));

        var result = await CreateService(gateway).SetEnabledAsync(true, _cts.Token);

        ResultAssert.Success(result, message);
        Assert.Equal(enabled, result.Data);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task StartJob_Success_ReturnsLocalizedMessage_WithoutLogging()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).StartJobAsync(JobName, _cts.Token);

        ResultAssert.Success(result, "BackgroundUpdateJobStarted");
        Assert.Empty(_logger.Entries);
    }

    private BackgroundUpdatesService CreateService(IMatrixGateway? gateway)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new BackgroundUpdatesService(session, _logger, new KeyLocalizer());
    }
}
