using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the result and logging contract of every <see cref="UserRedactionService"/> gateway method
/// (#600): no session, forwarded arguments, cancellation, gateway failure and refusal. The behavior of each method is
/// in <see cref="UserRedactionServiceTests"/>.
/// </summary>
public sealed class UserRedactionServiceCharacterizationTests : IDisposable
{
    // The line breaks prove that every log call sanitizes the untrusted strings.
    private const string UserId = "@ali\nce:example.com";
    private const string SanitizedUserId = "@ali ce:example.com";
    private const string RedactId = "redact\n1";
    private const string SanitizedRedactId = "redact 1";

    private sealed record Operation(
        string GatewayMethod,
        Func<UserRedactionService, CancellationToken, Task<OperationResult>> Invoke,
        Action<object?[], CancellationToken> AssertGatewayArgs,
        string FailureKey,
        string ErrorTemplate,
        Dictionary<string, object?> ErrorArguments,
        LogLevel RefusalLevel,
        string RefusalTemplate);

    private static readonly Dictionary<string, Operation> Operations = new()
    {
        ["Start"] = new("RedactUserEventsAsync",
            async (s, ct) => await s.StartRedactionAsync(UserId, new UserRedactionRequestViewModel(), ct),
            (args, ct) =>
            {
                Assert.Equal(UserId, args[0]);
                Assert.IsType<SynapseAdminUserRedactRequest>(args[1]);
                Assert.Equal(ct, args[2]);
            },
            "ErrorRedactingUserEvents", "Error starting the redaction of events of user {UserId}",
            new() { ["UserId"] = SanitizedUserId },
            // The admin started it: a refusal is the operation's error.
            LogLevel.Error, "Error starting the redaction of events of user {UserId}"),
        ["Status"] = new("GetUserRedactionStatusAsync",
            async (s, ct) => await s.GetRedactionStatusAsync(RedactId, ct),
            (args, ct) => Assert.Equal([RedactId, ct], args),
            "ErrorFetchingUserRedactionStatus", "Error getting the status of redaction {RedactId}",
            new() { ["RedactId"] = SanitizedRedactId },
            // Only the page's poll reads it.
            LogLevel.Debug, "The homeserver doesn't support reading the status of redaction {RedactId}"),
    };

    public static TheoryData<string> AllOperations => new(Operations.Keys);

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<UserRedactionService> _logger = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _cts.Dispose();
        _cache.Dispose();
    }

    private static IMatrixGateway Gateway(Action<Stub>? configure = null) => Stub.Create<IMatrixGateway>(s =>
    {
        s.On("get_HomeserverUrl", _ => "https://hs.example");
        configure?.Invoke(s);
    });

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
        stub.On("get_HomeserverUrl", _ => "https://hs.example");

        await operation.Invoke(CreateService(gateway), _cts.Token);

        var call = Assert.Single(stub.Calls, c => !c.Method.StartsWith("get_", StringComparison.Ordinal));
        Assert.Equal(operation.GatewayMethod, call.Method);
        operation.AssertGatewayArgs(call.Args, _cts.Token);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayCancels_ReturnsSilentCancellation_WithoutLogging(string name)
    {
        var gateway = Gateway(s => s.Fault(Operations[name].GatewayMethod, new TaskCanceledException()));

        var result = await Operations[name].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Cancelled(result);
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayThrows_FailsWithOperationKey_AndLogsError(string name)
    {
        var operation = Operations[name];
        var error = new InvalidOperationException("boom");
        var gateway = Gateway(s => s.Fault(operation.GatewayMethod, error));

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, operation.FailureKey);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Error, operation.ErrorTemplate), (entry.Level, entry.Template));
        Assert.Equal(operation.ErrorArguments, entry.Arguments);
        Assert.Same(error, entry.Exception);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayRefuses_ReturnsTheNotSupportedWarning(string name)
    {
        var operation = Operations[name];
        var refusal = new OperationNotSupportedByHomeserverException(operation.GatewayMethod);
        var gateway = Gateway(s => s.Fault(operation.GatewayMethod, refusal));

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, "OperationNotSupportedByHomeserver", Severity.Warning);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((operation.RefusalLevel, operation.RefusalTemplate), (entry.Level, entry.Template));
        Assert.Equal(operation.ErrorArguments, entry.Arguments);
    }

    /// <summary>A failed or refused start tracks nothing.</summary>
    [Theory]
    [InlineData("boom")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task Start_Fails_TracksNothing(string kind)
    {
        Exception error = kind switch
        {
            "refused" => new OperationNotSupportedByHomeserverException("RedactUserEventsAsync"),
            "cancelled" => new TaskCanceledException(),
            _ => new InvalidOperationException("boom")
        };
        var service = CreateService(Gateway(s => s.Fault("RedactUserEventsAsync", error)));

        await service.StartRedactionAsync(UserId, new UserRedactionRequestViewModel(), _cts.Token);

        Assert.Null(service.GetActiveRedactionId(UserId));
    }

    private static IMatrixSessionService Session(IMatrixGateway? gateway) =>
        Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));

    private UserRedactionService CreateService(IMatrixGateway? gateway) => new(Session(gateway), _logger, new KeyLocalizer(), _cache);
}
