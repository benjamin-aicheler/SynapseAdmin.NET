using System.Globalization;
using System.Resources;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
using User = SynapseAdmin.Models.Responses.SynapseAdminUserListResult.SynapseAdminUserListResultUser;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the result and logging contract of every <see cref="UserService"/> method (P3-11e): no
/// session, forwarded arguments, cancellation, gateway failure, null gateway result and success. Paging, media
/// totals, experimental features and the local-user check have their own suites; this one adds the error
/// handling and the parts of <see cref="UserService.GetUserDetailsAsync"/> that continue without a sub-result.
/// </summary>
public sealed class UserServiceCharacterizationTests : IDisposable
{
    // The line breaks prove that every log call sanitizes the untrusted strings.
    private const string UserId = "@ali\nce:example.com";
    private const string SanitizedUserId = "@ali ce:example.com";
    private const string Search = "ali\nce";
    private static readonly TimeSpan ExpireIn = TimeSpan.FromMinutes(5);
    private static readonly Dictionary<string, bool> Features = new() { ["msc3881"] = true };
    private static readonly Dictionary<string, object?> UserArgument = new() { ["UserId"] = SanitizedUserId };

    private sealed record Operation(
        string GatewayMethod,
        Func<UserService, CancellationToken, Task<OperationResult>> Invoke,
        Action<object?[], CancellationToken> AssertGatewayArgs,
        string FailureKey,
        string ErrorTemplate,
        Dictionary<string, object?> ErrorArguments,
        string? SuccessKey = null,
        string? InfoTemplate = null,
        Dictionary<string, object?>? InfoArguments = null,
        Action<Stub>? SetUpSuccess = null);

    private static Action<object?[], CancellationToken> Args(params object?[] expected) => (args, ct) => Assert.Equal(expected.Append(ct).ToArray(), args);

    private static readonly Dictionary<string, Operation> Operations = new()
    {
        ["List"] = new("GetUserListAsync", async (s, ct) => await s.GetUserListAsync(20, 10, "name", SortDirection.Ascending, Search, ct),
            Args(20, 10, "name", "f", Search), "ErrorFetchingUserList",
            "Error fetching user list (offset: {Offset}, limit: {Limit}, searchTerm: {SearchTerm})",
            new() { ["Offset"] = 20, ["Limit"] = 10, ["SearchTerm"] = "ali ce" }),
        ["Details"] = new("GetUserDetailsAsync", async (s, ct) => await s.GetUserDetailsAsync(UserId, ct),
            Args(UserId), "ErrorFetchingUserDetails", "Error fetching user details for {UserId}", UserArgument),
        ["MediaPage"] = new("GetUserMediaAsync", async (s, ct) => await s.GetUserMediaPageAsync(UserId, 30, 20, ct),
            Args(UserId, 30, 20, "created_ts", "b"), "ErrorFetchingMedia",
            "Error fetching media page (offset {Offset}, limit {Limit}) for user {UserId}",
            new() { ["Offset"] = 30, ["Limit"] = 20, ["UserId"] = SanitizedUserId }),
        ["MediaTotals"] = new("GetUserMediaStatisticsAsync", async (s, ct) => await s.GetUserMediaTotalsAsync(UserId, ct),
            Args(100, "media_length", "b", "ali\nce"), "ErrorFetchingMedia", "Error fetching media totals for user {UserId}", UserArgument),
        ["Deactivate"] = new("DeactivateUserAsync", (s, ct) => s.DeactivateUserAsync(UserId, true, ct),
            Args(UserId, true), "ErrorDeactivatingUser", "Error deactivating user {UserId}", UserArgument,
            "UserDeactivatedSuccessfully", "Successfully deactivated user {UserId} (erase: {Erase})",
            new() { ["UserId"] = SanitizedUserId, ["Erase"] = true }),
        ["QuarantineMedia"] = new("QuarantineMediaByUserIdAsync", (s, ct) => s.QuarantineMediaAsync(UserId, ct),
            Args(UserId), "ErrorQuarantiningMedia", "Error quarantining media for user {UserId}", UserArgument,
            "UserMediaQuarantinedSuccessfully", "Successfully quarantined media for user {UserId}", UserArgument),
        ["LoginAs"] = new("LoginAsUserAsync", async (s, ct) => await s.LoginAsUserAsync(UserId, ExpireIn, ct),
            Args(UserId, ExpireIn), "ErrorLoggingInAsUser", "Error performing shadow login for user {UserId}", UserArgument,
            "ShadowLoginSuccessful", "Admin successfully performed shadow login as user {UserId}", UserArgument,
            s => s.On("LoginAsUserAsync", _ => Task.FromResult(new LoginResponse { AccessToken = "token" }))),
        ["ServerNotice"] = new("SendServerNoticeAsync", (s, ct) => s.SendServerNoticeAsync(UserId, "hello", ct),
            (args, ct) =>
            {
                Assert.Equal(UserId, args[0]);
                Assert.Equal("""{"msgtype":"m.text","body":"hello"}""", JsonSerializer.Serialize(args[1]));
                Assert.Equal(new object?[] { null, null, ct }, args[2..]);
            },
            "ErrorSendingServerNotice", "Error sending server notice to user {UserId}", UserArgument,
            "ServerNoticeSentSuccessfully", "Successfully sent server notice to user {UserId}", UserArgument),
        ["TopMedia"] = new("GetUserMediaStatisticsAsync", async (s, ct) => await s.GetTopMediaUsersAsync(5, ct),
            Args(5, "media_length", "b", null), "ErrorFetchingTopMediaUsers", "Error fetching top media users", []),
        ["Create"] = new("UpdateUserAsync", (s, ct) => s.CreateUserAsync(new() { UserId = UserId, Password = "pw", DisplayName = "Al", Admin = true }, ct),
            (args, ct) =>
            {
                Assert.Equal(UserId, args[0]);
                var request = Assert.IsType<SynapseAdminUserUpsertRequest>(args[1]);
                Assert.Equal(("pw", "Al", (bool?)true, (bool?)false), (request.Password, request.DisplayName, request.Admin, request.Deactivated));
                Assert.Equal(ct, args[2]);
            },
            "ErrorCreatingUser", "Error creating user {UserId}", UserArgument,
            "UserCreatedSuccessfully", "Successfully created user {UserId}", UserArgument,
            s => s.On("UpdateUserAsync", _ => Task.FromResult<User?>(new User { Name = UserId }))),
        ["Memberships"] = new("GetUserMembershipsAsync", async (s, ct) => await s.GetUserMembershipsAsync(UserId, ct),
            Args(UserId), "ErrorFetchingUserMemberships", "Error fetching memberships for user {UserId}", UserArgument),
        ["ExperimentalFeatures"] = new("GetUserExperimentalFeaturesAsync", async (s, ct) => await s.GetUserExperimentalFeaturesAsync(UserId, ct),
            Args(UserId), "ErrorFetchingExperimentalFeatures", "Error fetching experimental features for user {UserId}", UserArgument),
        ["SetExperimentalFeatures"] = new("SetUserExperimentalFeaturesAsync", (s, ct) => s.SetUserExperimentalFeaturesAsync(UserId, Features, ct),
            (args, ct) =>
            {
                Assert.Equal(UserId, args[0]);
                Assert.Same(Features, Assert.IsType<SynapseAdminExperimentalFeaturesRequest>(args[1]).Features);
                Assert.Equal(ct, args[2]);
            },
            "ErrorUpdatingExperimentalFeatures", "Error updating experimental features for user {UserId}", UserArgument,
            "ExperimentalFeaturesUpdatedSuccessfully", "Successfully updated experimental features for user {UserId}", UserArgument),
        ["RenewValidity"] = new("RenewAccountValidityAsync", async (s, ct) => await s.RenewAccountValidityAsync(new() { UserId = UserId, ExpirationTs = 123, EnableRenewalEmails = false }, ct),
            (args, ct) =>
            {
                var request = Assert.IsType<SynapseAdminAccountValidityRequest>(args[0]);
                Assert.Equal((UserId, (long?)123, (bool?)false), (request.UserId, request.ExpirationTs, request.EnableRenewalEmails));
                Assert.Equal(ct, args[1]);
            },
            "ErrorRenewingAccountValidity", "Error renewing account validity for user {UserId}", UserArgument,
            "AccountValidityRenewedSuccessfully", "Successfully renewed account validity for user {UserId} (new expiration_ts: {ExpirationTs})",
            new() { ["UserId"] = SanitizedUserId, ["ExpirationTs"] = 456L },
            s => s.On("RenewAccountValidityAsync", _ => Task.FromResult<SynapseAdminAccountValidityResponse?>(new() { ExpirationTs = 456 }))),
    };

    public static TheoryData<string> AllOperations => new(Operations.Keys);
    // Details reads three endpoints and Create checks for an existing account first: each has its own test.
    public static TheoryData<string> SingleCallOperations => new(Operations.Keys.Where(k => k is not ("Details" or "Create")));
    public static TheoryData<string> ActionOperations => new(Operations.Where(o => o.Value.SuccessKey != null).Select(o => o.Key));

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<UserService> _logger = new();

    public void Dispose() => _cts.Dispose();

    [Theory, MemberData(nameof(AllOperations))]
    public async Task NoSession_FailsNotAuthenticated_WithoutLogging(string name)
    {
        var result = await Operations[name].Invoke(CreateService(gateway: null), _cts.Token);

        ResultAssert.Failure(result, "NotAuthenticated");
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(SingleCallOperations))]
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
        AssertSingleLog(LogLevel.Error, operation.ErrorTemplate, operation.ErrorArguments, error);
    }

    [Theory, MemberData(nameof(ActionOperations))]
    public async Task Action_Success_ReturnsLocalizedMessage_AndLogsInformation(string name)
    {
        var operation = Operations[name];
        var gateway = Stub.Create<IMatrixGateway>(s => operation.SetUpSuccess?.Invoke(s));

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Success(result, operation.SuccessKey!);
        AssertSingleLog(LogLevel.Information, operation.InfoTemplate!, operation.InfoArguments!, exception: null);
        switch (result)
        {
            case OperationResult<string> login: Assert.Equal("token", login.Data); break;
            case OperationResult<long> renew: Assert.Equal(456, renew.Data); break;
        }
    }

    [Theory]
    [InlineData("List")]
    [InlineData("MediaPage")]
    [InlineData("TopMedia")]
    [InlineData("Memberships")]
    [InlineData("ExperimentalFeatures")]
    public async Task CollectionRead_NullGatewayResult_IsEmpty(string name)
    {
        var result = await Operations[name].Invoke(CreateService(Stub.Create<IMatrixGateway>()), _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Empty(_logger.Entries);
        switch (result)
        {
            case OperationResult<(int Total, List<UserListViewModel> Users)> list: Assert.Equal((0, 0), (list.Data.Total, list.Data.Users.Count)); break;
            case OperationResult<UserMediaPageViewModel> page: Assert.Equal((30, 0, 0), (page.Data!.Offset, page.Data.TotalCount, page.Data.Items.Count)); break;
            case OperationResult<List<UserMediaStatisticsViewModel>> top: Assert.Empty(top.Data!); break;
            case OperationResult<List<UserMembershipViewModel>> memberships: Assert.Empty(memberships.Data!); break;
            case OperationResult<List<UserExperimentalFeatureViewModel>> features: Assert.Empty(features.Data!); break;
            default: Assert.Fail($"Unexpected result type {result.GetType()}"); break;
        }
    }

    [Theory]
    [InlineData("Details", "UserNotFound")]
    [InlineData("Create", "ErrorCreatingUser")]
    [InlineData("RenewValidity", "ErrorRenewingAccountValidity")]
    public async Task SingleResource_NullGatewayResult_Fails_WithoutLogging(string name, string key)
    {
        var result = await Operations[name].Invoke(CreateService(Stub.Create<IMatrixGateway>()), _cts.Token);

        ResultAssert.Failure(result, key);
        Assert.Empty(_logger.Entries);
    }

    /// <summary>
    /// <c>PUT /_synapse/admin/v2/users/{userId}</c> modifies an account that exists, so creating checks first and sends
    /// the <c>PUT</c> only when the homeserver doesn't know the user.
    /// </summary>
    [Fact]
    public async Task Create_ChecksForAnExistingUser_ThenSendsThePut()
    {
        var operation = Operations["Create"];
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        await operation.Invoke(CreateService(gateway), _cts.Token);

        Assert.Equal(["GetUserDetailsAsync", "UpdateUserAsync"], stub.Calls.Select(c => c.Method));
        Assert.Equal(new object?[] { UserId, _cts.Token }, stub.Calls[0].Args);
        operation.AssertGatewayArgs(stub.Calls[1].Args, _cts.Token);
    }

    /// <summary>
    /// The <c>PUT</c> would remove an admin's rights, reactivate a deactivated account and replace the password, and
    /// still report success, so an existing user ID is refused before anything is sent.
    /// </summary>
    [Fact]
    public async Task Create_ExistingUser_FailsWithAWarning_WithoutThePut()
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        stub.On("GetUserDetailsAsync", _ => Task.FromResult<User?>(new User { Name = UserId, Admin = true }));

        var result = await Operations["Create"].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, "UserAlreadyExists", Severity.Warning);
        Assert.Equal(["GetUserDetailsAsync"], stub.Calls.Select(c => c.Method));
        AssertSingleLog(LogLevel.Warning, "Refused to create user {UserId}: the account already exists", UserArgument, exception: null);
    }

    /// <summary>When the check fails, nothing says the user is new, so the <c>PUT</c> isn't sent.</summary>
    [Fact]
    public async Task Create_CheckFails_FailsWithTheCreateKey_WithoutThePut()
    {
        var error = new InvalidOperationException("boom");
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        stub.Fault("GetUserDetailsAsync", error);

        var result = await Operations["Create"].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, "ErrorCreatingUser");
        Assert.Equal(["GetUserDetailsAsync"], stub.Calls.Select(c => c.Method));
        AssertSingleLog(LogLevel.Error, "Error creating user {UserId}", UserArgument, error);
    }

    [Fact]
    public async Task MediaTotals_NullStatistics_FailsAndLogsAWarning()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).GetUserMediaTotalsAsync(UserId, _cts.Token);

        ResultAssert.Failure(result, "ErrorFetchingMedia");
        AssertSingleLog(LogLevel.Warning, "Could not determine media totals for user {UserId}: not among the first {Limit} statistics matches.",
            new() { ["UserId"] = SanitizedUserId, ["Limit"] = 100 }, exception: null);
    }

    // The gateway's login response is not nullable. A null one is logged as a successful login first, then fails
    // on the access token, so the error is logged and reported as well.
    [Fact]
    public async Task LoginAs_NullGatewayResult_LogsTheLoginThenFails()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).LoginAsUserAsync(UserId, ExpireIn, _cts.Token);

        ResultAssert.Failure(result, "ErrorLoggingInAsUser");
        Assert.Equal([LogLevel.Information, LogLevel.Error], _logger.Entries.Select(e => e.Level));
        Assert.IsType<NullReferenceException>(_logger.Entries[1].Exception);
    }

    // The token is real and expires (#539): the message names its lifetime in minutes, in every language.
    [Fact]
    public async Task LoginAs_Success_MessageNamesTheTokenLifetime()
    {
        using var services = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
        var localizer = services.GetRequiredService<IStringLocalizer<SharedResources>>();
        var gateway = Stub.Create<IMatrixGateway>(s =>
            s.On("LoginAsUserAsync", _ => Task.FromResult(new LoginResponse { AccessToken = "token" })));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));

        var result = await new UserService(session, _logger, localizer).LoginAsUserAsync(UserId, ExpireIn, _cts.Token);

        Assert.True(result.Success);
        Assert.Equal(localizer["ShadowLoginSuccessful", 5].Value, result.Message);
        Assert.Contains("5", result.Message);
        var resources = new ResourceManager(typeof(SharedResources).FullName!, typeof(SharedResources).Assembly);
        foreach (var culture in new[] { "en", "de", "fr" })
        {
            Assert.Contains("{0}", resources.GetString("ShadowLoginSuccessful", new CultureInfo(culture)));
        }
    }

    // The log reports the clamped page, not the requested one.
    [Fact]
    public async Task MediaPage_Failure_LogsTheClampedOffsetAndLimit()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.Fault("GetUserMediaAsync", new InvalidOperationException()));

        await CreateService(gateway).GetUserMediaPageAsync(UserId, -5, 500, _cts.Token);

        Assert.Equal(new Dictionary<string, object?> { ["Offset"] = 0, ["Limit"] = 100, ["UserId"] = SanitizedUserId },
            Assert.Single(_logger.Entries).Arguments);
    }

    [Fact]
    public async Task Details_Success_IncludesMembershipsAndExperimentalFeatures_WithoutLogging()
    {
        var gateway = DetailsGateway(s => s
            .On("GetUserMembershipsAsync", _ => Task.FromResult<SynapseAdminUserMembershipsResponse?>(new() { Memberships = new() { ["!r:example.com"] = "join" } }))
            .On("GetUserExperimentalFeaturesAsync", _ => Task.FromResult<SynapseAdminExperimentalFeaturesResponse?>(new() { Features = Features })));

        var result = await CreateService(gateway).GetUserDetailsAsync(UserId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Equal(("!r:example.com", "join"), (Assert.Single(result.Data!.Memberships).RoomId, result.Data.Memberships[0].Membership));
        Assert.Equal(("msc3881", true), (Assert.Single(result.Data.ExperimentalFeatures!).FeatureKey, result.Data.ExperimentalFeatures![0].Enabled));
        Assert.Empty(_logger.Entries);
    }

    // The memberships go through GetUserMembershipsAsync, which handles its own errors and cancellation.
    [Fact]
    public async Task Details_MembershipsFail_ContinuesWithoutMemberships_AndLogsTheMembershipsError()
    {
        var error = new InvalidOperationException("boom");

        var result = await CreateService(DetailsGateway(s => s.Fault("GetUserMembershipsAsync", error))).GetUserDetailsAsync(UserId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Empty(result.Data!.Memberships);
        AssertSingleLog(LogLevel.Error, "Error fetching memberships for user {UserId}", UserArgument, error);
    }

    [Fact]
    public async Task Details_MembershipsCancelled_ContinuesWithoutMemberships()
    {
        var result = await CreateService(DetailsGateway(s => s.Fault("GetUserMembershipsAsync", new TaskCanceledException()))).GetUserDetailsAsync(UserId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Empty(result.Data!.Memberships);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Details_ExperimentalFeaturesFail_ContinueWithoutThem_AndLogAWarning()
    {
        var error = new InvalidOperationException("boom");

        var result = await CreateService(DetailsGateway(s => s.Fault("GetUserExperimentalFeaturesAsync", error))).GetUserDetailsAsync(UserId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Null(result.Data!.ExperimentalFeatures);
        AssertSingleLog(LogLevel.Warning, "Failed to fetch experimental features for user {UserId}. Continuing without experimental features.", UserArgument, error);
    }

    [Fact]
    public async Task Details_ExperimentalFeaturesCancelled_CancelTheDetails()
    {
        var result = await CreateService(DetailsGateway(s => s.Fault("GetUserExperimentalFeaturesAsync", new TaskCanceledException()))).GetUserDetailsAsync(UserId, _cts.Token);

        ResultAssert.Cancelled(result);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Details_RemoteUser_WarnsWithoutQueryingTheUser()
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        stub.On("get_ServerName", _ => "other.example");

        var result = await CreateService(gateway).GetUserDetailsAsync(UserId, _cts.Token);

        ResultAssert.Failure(result, "CannotQueryRemoteUser", Severity.Warning);
        Assert.Equal(["get_ServerName"], stub.Calls.Select(c => c.Method));
        Assert.Empty(_logger.Entries);
    }

    private static IMatrixGateway DetailsGateway(Action<Stub> configure) => Stub.Create<IMatrixGateway>(s =>
    {
        s.On("get_ServerName", _ => "example.com").On("GetUserDetailsAsync", _ => Task.FromResult<User?>(new User { Name = UserId }));
        configure(s);
    });

    private UserService CreateService(IMatrixGateway? gateway)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        return new UserService(session, _logger, new KeyLocalizer());
    }

    private void AssertSingleLog(LogLevel level, string template, Dictionary<string, object?> arguments, Exception? exception)
    {
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal((level, template), (entry.Level, entry.Template));
        Assert.Equal(arguments, entry.Arguments);
        Assert.Same(exception, entry.Exception);
    }
}
