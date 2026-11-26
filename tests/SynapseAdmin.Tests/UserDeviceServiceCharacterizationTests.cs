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
using Device = SynapseAdmin.Models.Responses.SynapseAdminUserDeviceListResult.SynapseAdminUserDevice;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the result and logging contract of every <see cref="UserDeviceService"/> method (#598): no
/// session, forwarded arguments, cancellation, gateway failure, refusal, null gateway result and success. Also the
/// rename validation, the refusal to delete this session's own device, and that no IP address, user agent or device
/// name reaches the log.
/// </summary>
public sealed class UserDeviceServiceCharacterizationTests : IDisposable
{
    // The line breaks prove that every log call sanitizes the untrusted strings.
    private const string UserId = "@ali\nce:example.com";
    private const string SanitizedUserId = "@ali ce:example.com";
    private const string DeviceId = "PHO\nNE";
    private const string SanitizedDeviceId = "PHO NE";
    private const string AdminId = "@admin:example.com";
    private const string SessionDevice = "APPSESSION";
    private const string Ip = "192.0.2.77";
    private const string UserAgent = "SecretAgent/1.0";
    private const string DeviceName = "Alice's phone";
    private static readonly string[] DeviceIds = [DeviceId, "TABLET"];
    private static readonly Dictionary<string, object?> UserArgument = new() { ["UserId"] = SanitizedUserId };

    private sealed record Operation(
        string GatewayMethod,
        Func<UserDeviceService, CancellationToken, Task<OperationResult>> Invoke,
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
        ["Devices"] = new("GetUserDevicesAsync", async (s, ct) => await s.GetUserDevicesAsync(UserId, ct),
            Args(UserId), "ErrorFetchingDevices", "Error fetching devices for user {UserId}", UserArgument),
        ["Connections"] = new("GetUserWhoisAsync", async (s, ct) => await s.GetUserConnectionsAsync(UserId, ct),
            Args(UserId), "ErrorFetchingConnections", "Error fetching recent connections for user {UserId}", UserArgument),
        // The new name isn't logged: device names are personal data.
        ["Rename"] = new("RenameUserDeviceAsync", (s, ct) => s.RenameUserDeviceAsync(UserId, DeviceId, DeviceName, ct),
            (args, ct) =>
            {
                Assert.Equal((UserId, DeviceId), (args[0], args[1]));
                Assert.Equal(DeviceName, Assert.IsType<SynapseAdminDeviceUpdateRequest>(args[2]).DisplayName);
                Assert.Equal(ct, args[3]);
            },
            "ErrorRenamingDevice", "Error renaming device {DeviceId} of user {UserId}",
            new() { ["DeviceId"] = SanitizedDeviceId, ["UserId"] = SanitizedUserId },
            "DeviceRenamedSuccessfully", "Successfully renamed device {DeviceId} of user {UserId}",
            new() { ["DeviceId"] = SanitizedDeviceId, ["UserId"] = SanitizedUserId }),
        ["Delete"] = new("DeleteUserDevicesAsync", (s, ct) => s.DeleteUserDevicesAsync(UserId, DeviceIds, ct),
            (args, ct) =>
            {
                Assert.Equal(UserId, args[0]);
                Assert.Equal(DeviceIds, Assert.IsType<SynapseAdminDeleteDevicesRequest>(args[1]).Devices);
                Assert.Equal(ct, args[2]);
            },
            "ErrorDeletingDevices", "Error deleting devices {DeviceIds} of user {UserId}",
            new() { ["DeviceIds"] = "PHO NE, TABLET", ["UserId"] = SanitizedUserId },
            "DevicesDeletedSuccessfully", "Successfully deleted devices {DeviceIds} of user {UserId}",
            new() { ["DeviceIds"] = "PHO NE, TABLET", ["UserId"] = SanitizedUserId }),
    };

    public static TheoryData<string> AllOperations => new(Operations.Keys);
    public static TheoryData<string> ActionOperations => new(Operations.Where(o => o.Value.SuccessKey != null).Select(o => o.Key));

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<UserDeviceService> _logger = new();

    public void Dispose() => _cts.Dispose();

    /// <summary>A gateway signed in as another admin, so the own-device rule doesn't apply.</summary>
    private static IMatrixGateway Gateway(Action<Stub>? configure = null) => Stub.Create<IMatrixGateway>(s =>
    {
        s.On("get_UserId", _ => AdminId).On("get_DeviceId", _ => SessionDevice);
        configure?.Invoke(s);
    });

    private static IMatrixGateway Gateway(out Stub stub)
    {
        var gateway = Stub.Create<IMatrixGateway>(out stub);
        stub.On("get_UserId", _ => AdminId).On("get_DeviceId", _ => SessionDevice);
        return gateway;
    }

    private static List<string> GatewayCalls(Stub stub) => stub.Calls.Select(c => c.Method).Where(m => !m.StartsWith("get_", StringComparison.Ordinal)).ToList();

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
        var gateway = Gateway(out var stub);

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
        AssertSingleLog(LogLevel.Error, operation.ErrorTemplate, operation.ErrorArguments, error);
    }

    /// <summary>The tabs only render with the admin API, so every refusal is logged as the operation's error.</summary>
    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayRefuses_ReturnsTheNotSupportedWarning_AndLogsError(string name)
    {
        var operation = Operations[name];
        var refusal = new OperationNotSupportedByHomeserverException(operation.GatewayMethod);
        var gateway = Gateway(s => s.Fault(operation.GatewayMethod, refusal));

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, "OperationNotSupportedByHomeserver", Severity.Warning);
        AssertSingleLog(LogLevel.Error, operation.ErrorTemplate, operation.ErrorArguments, refusal);
    }

    [Theory, MemberData(nameof(ActionOperations))]
    public async Task Action_Success_ReturnsLocalizedMessage_AndLogsInformation(string name)
    {
        var operation = Operations[name];

        var result = await operation.Invoke(CreateService(Gateway()), _cts.Token);

        ResultAssert.Success(result, operation.SuccessKey!);
        AssertSingleLog(LogLevel.Information, operation.InfoTemplate!, operation.InfoArguments!, exception: null);
    }

    [Theory]
    [InlineData("Devices")]
    [InlineData("Connections")]
    public async Task CollectionRead_NullGatewayResult_IsEmpty(string name)
    {
        var result = await Operations[name].Invoke(CreateService(Gateway()), _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Empty(_logger.Entries);
        switch (result)
        {
            case OperationResult<List<UserDeviceViewModel>> devices: Assert.Empty(devices.Data!); break;
            case OperationResult<List<UserConnectionViewModel>> connections: Assert.Empty(connections.Data!); break;
            default: Assert.Fail($"Unexpected result type {result.GetType()}"); break;
        }
    }

    [Fact]
    public async Task Devices_MapsTheAnswer_WithoutLoggingPersonalData()
    {
        var gateway = Gateway(s => s.On("GetUserDevicesAsync", _ => Task.FromResult<SynapseAdminUserDeviceListResult?>(new()
        {
            Devices = [new Device { DeviceId = DeviceId, DisplayName = DeviceName, LastSeenIp = Ip, LastSeenUserAgent = UserAgent, LastSeenTs = 1000 }],
            Total = 1
        })));

        var result = await CreateService(gateway).GetUserDevicesAsync(UserId, _cts.Token);

        var device = Assert.Single(result.Data!);
        Assert.Equal((DeviceId, DeviceName, Ip, UserAgent), (device.DeviceId, device.DisplayName, device.LastSeenIp, device.LastSeenUserAgent));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Connections_MapsTheAnswer()
    {
        var gateway = Gateway(s => s.On("GetUserWhoisAsync", _ => Task.FromResult<SynapseAdminWhoisResponse?>(new()
        {
            Devices = new() { [""] = new() { Sessions = [new() { Connections = [new() { Ip = Ip, UserAgent = UserAgent, LastSeen = 1000 }] }] } }
        })));

        var result = await CreateService(gateway).GetUserConnectionsAsync(UserId, _cts.Token);

        var connection = Assert.Single(result.Data!);
        Assert.Equal((Ip, UserAgent), (connection.Ip, connection.UserAgent));
    }

    /// <summary>
    /// A gateway failure whose message quotes the homeserver's answer: the log keeps the exception (as every failure
    /// does) but no template argument carries an IP, user agent or device name.
    /// </summary>
    [Theory, MemberData(nameof(AllOperations))]
    public async Task LogArguments_NeverCarryPersonalData(string name)
    {
        var operation = Operations[name];
        var gateway = Gateway(s => s.Fault(operation.GatewayMethod, new InvalidOperationException("boom")));

        await operation.Invoke(CreateService(gateway), _cts.Token);
        await Operations[name].Invoke(CreateService(Gateway()), _cts.Token);

        var values = _logger.Entries.SelectMany(e => e.Arguments.Values).Select(v => v?.ToString() ?? "").ToList();
        Assert.NotEmpty(values);
        Assert.DoesNotContain(values, v => v.Contains(Ip) || v.Contains(UserAgent) || v.Contains(DeviceName));
        Assert.DoesNotContain(_logger.Entries, e => e.Template.Contains(DeviceName));
    }

    // ---- Rename validation ----------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_EmptyName_FailsWithAWarning_WithoutARequest(string displayName)
    {
        var gateway = Gateway(out var stub);

        var result = await CreateService(gateway).RenameUserDeviceAsync(UserId, DeviceId, displayName, _cts.Token);

        ResultAssert.Failure(result, "DeviceNameRequired", Severity.Warning);
        Assert.Empty(GatewayCalls(stub));
        Assert.Empty(_logger.Entries);
    }

    /// <summary>Both servers count code points: 100 emoji (200 UTF-16 units) are allowed, 101 characters are not.</summary>
    [Theory]
    [InlineData(100, "a", true)]
    [InlineData(101, "a", false)]
    [InlineData(100, "\U0001F4F1", true)]
    [InlineData(101, "\U0001F4F1", false)]
    public async Task Rename_LengthLimit_CountsCodePoints(int count, string character, bool sent)
    {
        var gateway = Gateway(out var stub);
        var displayName = string.Concat(Enumerable.Repeat(character, count));

        var result = await CreateService(gateway).RenameUserDeviceAsync(UserId, DeviceId, displayName, _cts.Token);

        if (sent)
        {
            ResultAssert.Success(result, "DeviceRenamedSuccessfully");
            Assert.Equal(["RenameUserDeviceAsync"], GatewayCalls(stub));
        }
        else
        {
            ResultAssert.Failure(result, "DeviceNameTooLong", Severity.Warning);
            Assert.Empty(GatewayCalls(stub));
        }
    }

    // ---- Deleting this session's device -----------------------------------------------------------------

    [Fact]
    public async Task Delete_NoDevices_FailsWithAWarning_WithoutARequest()
    {
        var gateway = Gateway(out var stub);

        var result = await CreateService(gateway).DeleteUserDevicesAsync(UserId, [], _cts.Token);

        ResultAssert.Failure(result, "NoDevicesSelected", Severity.Warning);
        Assert.Empty(GatewayCalls(stub));
    }

    /// <summary>
    /// Deleting the device of this session's access token would sign the app out. While the session's device is
    /// unknown, no device of the signed-in admin's own account is deleted.
    /// </summary>
    [Theory]
    [InlineData(SessionDevice, new[] { SessionDevice })]
    [InlineData(SessionDevice, new[] { "OTHER", SessionDevice })]
    [InlineData(null, new[] { "OTHER" })]
    public async Task Delete_OwnSessionDevice_FailsWithAWarning_WithoutARequest(string? sessionDevice, string[] deviceIds)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        stub.On("get_UserId", _ => AdminId).On("get_DeviceId", _ => sessionDevice);

        var result = await CreateService(gateway).DeleteUserDevicesAsync(AdminId, deviceIds, _cts.Token);

        ResultAssert.Failure(result, "CannotDeleteOwnSessionDevice", Severity.Warning);
        Assert.Empty(GatewayCalls(stub));
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((LogLevel.Warning, "Refused to delete devices {DeviceIds} of user {UserId}: they include this session's device, or it is unknown"),
            (entry.Level, entry.Template));
    }

    /// <summary>The admin's other devices can go, and so can another user's device that happens to share the ID.</summary>
    [Theory]
    [InlineData(AdminId, "OTHER")]
    [InlineData("@bob:example.com", SessionDevice)]
    [InlineData("@Admin:example.com", SessionDevice)]
    public async Task Delete_NotTheSessionDevice_IsSent(string userId, string deviceId)
    {
        var gateway = Gateway(out var stub);

        var result = await CreateService(gateway).DeleteUserDevicesAsync(userId, [deviceId], _cts.Token);

        ResultAssert.Success(result, "DevicesDeletedSuccessfully");
        Assert.Equal(["DeleteUserDevicesAsync"], GatewayCalls(stub));
    }

    [Fact]
    public async Task Delete_AnotherUser_WhileTheSessionDeviceIsUnknown_IsSent()
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        stub.On("get_UserId", _ => AdminId);

        var result = await CreateService(gateway).DeleteUserDevicesAsync("@bob:example.com", ["PHONE"], _cts.Token);

        ResultAssert.Success(result, "DevicesDeletedSuccessfully");
        Assert.Equal(["DeleteUserDevicesAsync"], GatewayCalls(stub));
    }

    private static IMatrixSessionService Session(IMatrixGateway? gateway) =>
        Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));

    private UserDeviceService CreateService(IMatrixGateway? gateway) => new(Session(gateway), _logger, new KeyLocalizer());

    private void AssertSingleLog(LogLevel level, string template, Dictionary<string, object?> arguments, Exception? exception)
    {
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((level, template), (entry.Level, entry.Template));
        Assert.Equal(arguments, entry.Arguments);
        Assert.Same(exception, entry.Exception);
    }
}
