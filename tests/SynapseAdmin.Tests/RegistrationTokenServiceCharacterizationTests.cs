using Microsoft.Extensions.Logging;
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
using Token = SynapseAdmin.Models.Responses.SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the result and logging contract of every <see cref="RegistrationTokenService"/> method
/// (P3-11b): no session, forwarded arguments, cancellation, gateway failure, null gateway result and success.
/// </summary>
public sealed class RegistrationTokenServiceCharacterizationTests : IDisposable
{
    private const long Expiry = 1_700_000_000_000;

    private sealed record Operation(
        string GatewayMethod,
        Func<RegistrationTokenService, CancellationToken, Task<OperationResult>> Invoke,
        Action<object?[], CancellationToken> AssertGatewayArgs,
        string FailureKey,
        string ErrorTemplate,
        string? SuccessKey = null,
        string? InfoTemplate = null,
        Action<Stub>? SetUpSuccess = null);

    private static readonly Dictionary<string, Operation> Operations = new()
    {
        ["List"] = new("GetRegistrationTokensAsync", async (s, ct) => await s.GetRegistrationTokensAsync(ct),
            (args, ct) => Assert.Equal([ct], args),
            "ErrorLoadingTokens", "Error fetching registration tokens"),
        ["Create"] = new("CreateRegistrationTokenAsync", (s, ct) => s.CreateRegistrationTokenAsync(ViewModel("abc"), ct),
            (args, ct) =>
            {
                var request = Assert.IsType<SynapseAdminRegistrationTokenCreateRequest>(args[0]);
                Assert.Equal(((string?)"abc", (int?)3, (long?)Expiry, (int?)null), (request.Token, request.UsesAllowed, request.ExpiryTime, request.Length));
                Assert.Equal(ct, args[1]);
            },
            "ErrorCreatingToken", "Error creating registration token",
            "TokenCreatedSuccessfully", "Successfully created registration token",
            s => s.On("CreateRegistrationTokenAsync", _ => Task.FromResult<Token?>(new Token { Token = "abc" }))),
        ["Update"] = new("UpdateRegistrationTokenAsync", (s, ct) => s.UpdateRegistrationTokenAsync("abc", ViewModel("ignored"), ct),
            (args, ct) =>
            {
                Assert.Equal("abc", args[0]);
                var request = Assert.IsType<SynapseAdminRegistrationTokenUpdateRequest>(args[1]);
                Assert.Equal(((int?)3, (long?)Expiry), (request.UsesAllowed, request.ExpiryTime));
                Assert.Equal(ct, args[2]);
            },
            "ErrorUpdatingToken", "Error updating registration token",
            "TokenUpdatedSuccessfully", "Successfully updated registration token"),
        ["Delete"] = new("DeleteRegistrationTokenAsync", (s, ct) => s.DeleteRegistrationTokenAsync("abc", ct),
            (args, ct) => Assert.Equal(["abc", ct], args),
            "ErrorDeletingToken", "Error deleting registration token",
            "TokenDeletedSuccessfully", "Successfully deleted registration token"),
    };

    public static TheoryData<string> AllOperations => new(Operations.Keys);
    public static TheoryData<string> ActionOperations => new(Operations.Where(o => o.Value.SuccessKey != null).Select(o => o.Key));

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<RegistrationTokenService> _logger = new();

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
        operation.AssertGatewayArgs(call.Args, _cts.Token);
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
        AssertSingleLog(LogLevel.Error, operation.ErrorTemplate, error);
    }

    [Theory, MemberData(nameof(ActionOperations))]
    public async Task Action_Success_ReturnsLocalizedMessage_AndLogsInformation(string name)
    {
        var operation = Operations[name];
        var gateway = Stub.Create<IMatrixGateway>(s => operation.SetUpSuccess?.Invoke(s));

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Success(result, operation.SuccessKey!);
        AssertSingleLog(LogLevel.Information, operation.InfoTemplate!, exception: null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Create_BlankToken_IsSentAsNull_SoTheServerGeneratesOne(string token)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        await CreateService(gateway).CreateRegistrationTokenAsync(ViewModel(token), _cts.Token);

        var request = Assert.IsType<SynapseAdminRegistrationTokenCreateRequest>(Assert.Single(stub.CallsTo("CreateRegistrationTokenAsync"))[0]);
        Assert.Null(request.Token);
    }

    [Fact]
    public async Task Create_NullGatewayResult_FailsWithoutLogging()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).CreateRegistrationTokenAsync(ViewModel("abc"), _cts.Token);

        ResultAssert.Failure(result, "ErrorCreatingToken");
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task List_Success_MapsTheTokens_WithoutLogging()
    {
        List<Token> tokens = [new() { Token = "a", UsesAllowed = 1, Pending = 2, Completed = 3, ExpiryTime = Expiry }, new() { Token = "b" }];
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("GetRegistrationTokensAsync", _ => Task.FromResult(tokens)));

        var result = await CreateService(gateway).GetRegistrationTokensAsync(_cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(new (string, int?, int, int, long?)[] { ("a", 1, 2, 3, Expiry), ("b", null, 0, 0, null) },
            result.Data!.Select(t => (t.Token, t.UsesAllowed, t.Pending, t.Completed, t.ExpiryTime)));
        Assert.Empty(_logger.Entries);
    }

    // The gateway's list is not nullable, and the real gateway returns [] for a missing body. A null list fails
    // in the mapping, so it is logged and reported like a gateway failure.
    [Fact]
    public async Task List_NullGatewayResult_FailsAndLogsTheMappingError()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).GetRegistrationTokensAsync(_cts.Token);

        ResultAssert.Failure(result, "ErrorLoadingTokens");
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal("Error fetching registration tokens", entry.Template);
        Assert.IsType<ArgumentNullException>(entry.Exception);
    }

    private static RegistrationTokenViewModel ViewModel(string token) => new() { Token = token, UsesAllowed = 3, ExpiryTime = Expiry };

    private RegistrationTokenService CreateService(IMatrixGateway? gateway)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new RegistrationTokenService(session, _logger, new KeyLocalizer());
    }

    private void AssertSingleLog(LogLevel level, string template, Exception? exception)
    {
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(level, entry.Level);
        Assert.Equal(template, entry.Template);
        Assert.Empty(entry.Arguments);
        Assert.Same(exception, entry.Exception);
    }
}
