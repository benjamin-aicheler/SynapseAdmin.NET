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
        Action<Stub>? SetUpSuccess = null,
        Action<Stub>? Arrange = null);

    // The account edits read the user first and send the PUT only for an account the homeserver knows (#584).
    private static void UserExists(Stub s) => s.On("GetUserDetailsAsync", _ => Task.FromResult<User?>(new User { Name = UserId }));
    private static void PutReturnsUser(Stub s) => s.On("UpdateUserAsync", _ => Task.FromResult<User?>(new User { Name = UserId }));
    private static void UserIsDeactivated(Stub s) => s.On("GetUserDetailsAsync", _ => Task.FromResult<User?>(new User { Name = UserId, Deactivated = true }));
    private const string NewPassword = "new-secret";

    private static Operation Edit(string failureKey, string successKey, string state, string argument, bool value,
        Func<UserService, CancellationToken, Task<OperationResult>> invoke) =>
        new("UpdateUserAsync", invoke, (_, _) => { }, failureKey,
            $"Error setting the {state} of user {{UserId}} ({argument.ToLowerInvariant()}: {{{argument}}})",
            new() { ["UserId"] = SanitizedUserId, [argument] = value },
            successKey, $"Successfully set the {state} of user {{UserId}} ({argument.ToLowerInvariant()}: {{{argument}}})",
            new() { ["UserId"] = SanitizedUserId, [argument] = value },
            PutReturnsUser, UserExists);

    private static Action<object?[], CancellationToken> Args(params object?[] expected) => (args, ct) => Assert.Equal(expected.Append(ct).ToArray(), args);

    // Suspension and shadow-ban have their own endpoints, which create no account, so they send at once (#584).
    private static Operation State(string gatewayMethod, string failureKey, string successKey, string state, string argument, bool value,
        Func<UserService, CancellationToken, Task<OperationResult>> invoke)
    {
        var logged = argument == "ShadowBanned" ? "shadow-banned" : argument.ToLowerInvariant();
        return new(gatewayMethod, invoke, Args(UserId, value), failureKey,
            $"Error setting the {state} of user {{UserId}} ({logged}: {{{argument}}})",
            new() { ["UserId"] = SanitizedUserId, [argument] = value },
            successKey, $"Successfully set the {state} of user {{UserId}} ({logged}: {{{argument}}})",
            new() { ["UserId"] = SanitizedUserId, [argument] = value });
    }

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
        ["Lock"] = Edit("ErrorLockingUser", "UserLockedSuccessfully", "lock state", "Locked", true, (s, ct) => s.SetUserLockedAsync(UserId, true, ct)),
        ["Unlock"] = Edit("ErrorUnlockingUser", "UserUnlockedSuccessfully", "lock state", "Locked", false, (s, ct) => s.SetUserLockedAsync(UserId, false, ct)),
        ["MakeAdmin"] = Edit("ErrorGrantingAdmin", "AdminGrantedSuccessfully", "server admin state", "Admin", true, (s, ct) => s.SetUserAdminAsync(UserId, true, ct)),
        ["RemoveAdmin"] = Edit("ErrorRevokingAdmin", "AdminRevokedSuccessfully", "server admin state", "Admin", false, (s, ct) => s.SetUserAdminAsync(UserId, false, ct)),
        ["Suspend"] = State("SetUserSuspendedAsync", "ErrorSuspendingUser", "UserSuspendedSuccessfully", "suspension", "Suspended", true,
            (s, ct) => s.SetUserSuspendedAsync(UserId, true, ct)),
        ["Unsuspend"] = State("SetUserSuspendedAsync", "ErrorUnsuspendingUser", "UserUnsuspendedSuccessfully", "suspension", "Suspended", false,
            (s, ct) => s.SetUserSuspendedAsync(UserId, false, ct)),
        ["ShadowBan"] = State("SetUserShadowBannedAsync", "ErrorShadowBanningUser", "UserShadowBannedSuccessfully", "shadow-ban", "ShadowBanned", true,
            (s, ct) => s.SetUserShadowBannedAsync(UserId, true, ct)),
        ["LiftShadowBan"] = State("SetUserShadowBannedAsync", "ErrorLiftingShadowBan", "ShadowBanLiftedSuccessfully", "shadow-ban", "ShadowBanned", false,
            (s, ct) => s.SetUserShadowBannedAsync(UserId, false, ct)),
        // The password is never logged: the templates name only whether devices are logged out or a password was set.
        ["ResetPassword"] = new("ResetPasswordAsync", (s, ct) => s.ResetPasswordAsync(UserId, NewPassword, true, ct),
            (args, ct) =>
            {
                Assert.Equal(UserId, args[0]);
                var request = Assert.IsType<SynapseAdminResetPasswordRequest>(args[1]);
                Assert.Equal((NewPassword, true), (request.NewPassword, request.LogoutDevices));
                Assert.Equal(ct, args[2]);
            },
            "ErrorResettingPassword", "Error resetting the password of user {UserId} (logout devices: {LogoutDevices})",
            new() { ["UserId"] = SanitizedUserId, ["LogoutDevices"] = true },
            "PasswordResetSuccessfully", "Successfully reset the password of user {UserId} (logout devices: {LogoutDevices})",
            new() { ["UserId"] = SanitizedUserId, ["LogoutDevices"] = true }),
        ["Reactivate"] = new("UpdateUserAsync", (s, ct) => s.ReactivateUserAsync(UserId, NewPassword, ct), (_, _) => { },
            "ErrorReactivatingUser", "Error reactivating user {UserId} (with password: {WithPassword})",
            new() { ["UserId"] = SanitizedUserId, ["WithPassword"] = true },
            "UserReactivatedSuccessfully", "Successfully reactivated user {UserId} (with password: {WithPassword})",
            new() { ["UserId"] = SanitizedUserId, ["WithPassword"] = true },
            PutReturnsUser, UserIsDeactivated),
        // The display name isn't logged: only which fields the edit changes.
        ["EditProfile"] = new("UpdateUserAsync",
            (s, ct) => s.UpdateUserProfileAsync(UserId, new UserProfileEditViewModel { DisplayName = "Al", RemoveAvatar = true }, ct), (_, _) => { },
            "ErrorUpdatingUserProfile", "Error updating the profile of user {UserId} (display name: {DisplayNameChanged}, remove avatar: {RemoveAvatar})",
            new() { ["UserId"] = SanitizedUserId, ["DisplayNameChanged"] = true, ["RemoveAvatar"] = true },
            "UserProfileUpdatedSuccessfully", "Successfully updated the profile of user {UserId} (display name: {DisplayNameChanged}, remove avatar: {RemoveAvatar})",
            new() { ["UserId"] = SanitizedUserId, ["DisplayNameChanged"] = true, ["RemoveAvatar"] = true },
            PutReturnsUser, UserExists),
    };

    /// <summary>The account edits: each sends exactly one field after reading the user (#584).</summary>
    private static readonly Dictionary<string, string> EditBodies = new()
    {
        ["Lock"] = """{"locked":true}""",
        ["Unlock"] = """{"locked":false}""",
        ["MakeAdmin"] = """{"admin":true}""",
        ["RemoveAdmin"] = """{"admin":false}""",
        ["Reactivate"] = """{"password":"new-secret","deactivated":false}""",
        ["EditProfile"] = """{"displayname":"Al","avatar_url":""}""",
    };

    public static TheoryData<string> EditOperations => new(EditBodies.Keys);

    public static TheoryData<string> AllOperations => new(Operations.Keys);
    // Details reads three endpoints, and Create and the account edits check the account first: each has its own test.
    public static TheoryData<string> SingleCallOperations => new(Operations.Keys.Where(k => k is not ("Details" or "Create") && !EditBodies.ContainsKey(k)));
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
        var gateway = Stub.Create<IMatrixGateway>(s =>
        {
            Operations[name].Arrange?.Invoke(s);
            s.Fault(Operations[name].GatewayMethod, new TaskCanceledException());
        });

        var result = await Operations[name].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Cancelled(result);
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayThrows_FailsWithOperationKey_AndLogsError(string name)
    {
        var operation = Operations[name];
        var error = new InvalidOperationException("boom");
        var gateway = Stub.Create<IMatrixGateway>(s =>
        {
            operation.Arrange?.Invoke(s);
            s.Fault(operation.GatewayMethod, error);
        });

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, operation.FailureKey);
        AssertSingleLog(LogLevel.Error, operation.ErrorTemplate, operation.ErrorArguments, error);
    }

    // A read that a page runs by itself logs a refusal at Debug: the homeserver works as intended (#559). Every other
    // refused operation logs its error, as for any failure.
    private static readonly Dictionary<string, (string Template, Dictionary<string, object?> Arguments)> RefusalsLoggedAtDebug = new()
    {
        ["List"] = ("The homeserver doesn't support sorting the user list by {OrderBy}", new() { ["OrderBy"] = "name" }),
        ["ExperimentalFeatures"] = ("The homeserver doesn't support experimental features for user {UserId}", new() { ["UserId"] = SanitizedUserId }),
    };

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayRefuses_ReturnsTheNotSupportedWarning_AndLogsAtTheOperationsLevel(string name)
    {
        var operation = Operations[name];
        var refusal = new OperationNotSupportedByHomeserverException(operation.GatewayMethod);
        var gateway = Stub.Create<IMatrixGateway>(s =>
        {
            operation.Arrange?.Invoke(s);
            s.Fault(operation.GatewayMethod, refusal);
        });

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, "OperationNotSupportedByHomeserver", Severity.Warning);
        var entry = Assert.Single(_logger.Entries);
        if (RefusalsLoggedAtDebug.TryGetValue(name, out var debug))
        {
            Assert.Equal((LogLevel.Debug, debug.Template), (entry.Level, entry.Template));
            Assert.Equal(debug.Arguments, entry.Arguments);
            Assert.Null(entry.Exception);
        }
        else
        {
            Assert.Equal((LogLevel.Error, operation.ErrorTemplate), (entry.Level, entry.Template));
            Assert.Equal(operation.ErrorArguments, entry.Arguments);
            Assert.Same(refusal, entry.Exception);
        }
    }

    [Theory, MemberData(nameof(ActionOperations))]
    public async Task Action_Success_ReturnsLocalizedMessage_AndLogsInformation(string name)
    {
        var operation = Operations[name];
        var gateway = Stub.Create<IMatrixGateway>(s =>
        {
            operation.Arrange?.Invoke(s);
            operation.SetUpSuccess?.Invoke(s);
        });

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

    /// <summary>
    /// <c>PUT /_synapse/admin/v2/users/{userId}</c> creates a user it doesn't know, so an edit reads the account first,
    /// then sends exactly its own field: no other key, above all no <c>admin</c> unless the admin toggle changed (#584).
    /// </summary>
    [Theory, MemberData(nameof(EditOperations))]
    public async Task Edit_ReadsTheUser_ThenSendsOnlyItsField(string name)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        Operations[name].Arrange!(stub);
        Operations[name].SetUpSuccess!(stub);

        await Operations[name].Invoke(CreateService(gateway), _cts.Token);

        Assert.Equal(["GetUserDetailsAsync", "UpdateUserAsync"], stub.Calls.Select(c => c.Method));
        Assert.Equal(new object?[] { UserId, _cts.Token }, stub.Calls[0].Args);
        Assert.Equal(UserId, stub.Calls[1].Args[0]);
        var request = Assert.IsType<SynapseAdminUserUpsertRequest>(stub.Calls[1].Args[1]);
        // MatrixHttpClient omits null fields, as these options do.
        Assert.Equal(EditBodies[name], JsonSerializer.Serialize(request, OmitNulls));
        Assert.Equal(_cts.Token, stub.Calls[1].Args[2]);
    }

    private static readonly JsonSerializerOptions OmitNulls =
        new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>An account the homeserver doesn't know isn't edited: the PUT would create it.</summary>
    [Theory, MemberData(nameof(EditOperations))]
    public async Task Edit_UnknownUser_FailsNotFound_WithoutThePut(string name)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        var result = await Operations[name].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, "UserNotFound");
        Assert.Equal(["GetUserDetailsAsync"], stub.Calls.Select(c => c.Method));
        Assert.Empty(_logger.Entries);
    }

    /// <summary>When the read fails, nothing says the account exists, so the PUT isn't sent.</summary>
    [Theory, MemberData(nameof(EditOperations))]
    public async Task Edit_ReadFails_FailsWithTheEditKey_WithoutThePut(string name)
    {
        var operation = Operations[name];
        var error = new InvalidOperationException("boom");
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        stub.Fault("GetUserDetailsAsync", error);

        var result = await operation.Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, operation.FailureKey);
        Assert.Equal(["GetUserDetailsAsync"], stub.Calls.Select(c => c.Method));
        AssertSingleLog(LogLevel.Error, operation.ErrorTemplate, operation.ErrorArguments, error);
    }

    /// <summary>A PUT that returns no account fails with the operation's key (#395), without logging.</summary>
    [Theory, MemberData(nameof(EditOperations))]
    public async Task Edit_NullPutResult_Fails_WithoutLogging(string name)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        Operations[name].Arrange!(stub);

        var result = await Operations[name].Invoke(CreateService(gateway), _cts.Token);

        ResultAssert.Failure(result, Operations[name].FailureKey);
        Assert.Empty(_logger.Entries);
    }

    /// <summary>Without a new password, the reactivation sends only <c>deactivated</c>; an empty one counts as none.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Reactivate_WithoutAPassword_SendsOnlyDeactivated(string? password)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        UserIsDeactivated(stub);
        PutReturnsUser(stub);

        var result = await CreateService(gateway).ReactivateUserAsync(UserId, password, _cts.Token);

        ResultAssert.Success(result, "UserReactivatedSuccessfully");
        var request = Assert.IsType<SynapseAdminUserUpsertRequest>(stub.CallsTo("UpdateUserAsync").Single()[1]);
        Assert.Equal("""{"deactivated":false}""", JsonSerializer.Serialize(request, OmitNulls));
        AssertSingleLog(LogLevel.Information, "Successfully reactivated user {UserId} (with password: {WithPassword})",
            new() { ["UserId"] = SanitizedUserId, ["WithPassword"] = false }, exception: null);
    }

    /// <summary>
    /// An account that is active again (reactivated elsewhere since the page loaded) isn't sent the PUT: with a password
    /// it would replace the password and, as <c>logout_devices</c> defaults to <c>true</c>, log out all its devices.
    /// </summary>
    [Fact]
    public async Task Reactivate_ActiveAccount_FailsWithAWarning_WithoutThePut()
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        UserExists(stub);

        var result = await CreateService(gateway).ReactivateUserAsync(UserId, NewPassword, _cts.Token);

        ResultAssert.Failure(result, "UserAlreadyActive", Severity.Warning);
        Assert.Equal(["GetUserDetailsAsync"], stub.Calls.Select(c => c.Method));
        AssertSingleLog(LogLevel.Warning, "Refused to reactivate user {UserId}: the account is active", UserArgument, exception: null);
    }

    /// <summary>A profile edit sends only what changed: an emptied name as <c>""</c>, which removes it (#584).</summary>
    [Theory]
    [InlineData("Al", false, """{"displayname":"Al"}""")]
    [InlineData("", false, """{"displayname":""}""")]
    [InlineData(null, true, """{"avatar_url":""}""")]
    public async Task EditProfile_SendsOnlyTheChangedFields(string? displayName, bool removeAvatar, string body)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        UserExists(stub);
        PutReturnsUser(stub);

        var result = await CreateService(gateway).UpdateUserProfileAsync(UserId,
            new UserProfileEditViewModel { DisplayName = displayName, RemoveAvatar = removeAvatar }, _cts.Token);

        ResultAssert.Success(result, "UserProfileUpdatedSuccessfully");
        var request = Assert.IsType<SynapseAdminUserUpsertRequest>(stub.CallsTo("UpdateUserAsync").Single()[1]);
        Assert.Equal(body, JsonSerializer.Serialize(request, OmitNulls));
    }

    /// <summary>Without a change there is nothing to send, so nothing is read or sent either.</summary>
    [Fact]
    public async Task EditProfile_NoChange_SendsNothing()
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        var result = await CreateService(gateway).UpdateUserProfileAsync(UserId, new UserProfileEditViewModel(), _cts.Token);

        ResultAssert.Success(result, "UserProfileUnchanged");
        Assert.Empty(stub.Calls);
        Assert.Empty(_logger.Entries);
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

    /// <summary>A homeserver without per-user features (Tuwunel) refuses the read: the details show none, logged at Debug.</summary>
    [Fact]
    public async Task Details_ExperimentalFeaturesRefused_ContinueWithoutThem_AndLogAtDebug()
    {
        var refusal = new OperationNotSupportedByHomeserverException("GetUserExperimentalFeaturesAsync");

        var result = await CreateService(DetailsGateway(s => s.Fault("GetUserExperimentalFeaturesAsync", refusal))).GetUserDetailsAsync(UserId, _cts.Token);

        ResultAssert.Success(result, "");
        Assert.Null(result.Data!.ExperimentalFeatures);
        AssertSingleLog(LogLevel.Debug, "The homeserver doesn't support experimental features for user {UserId}", UserArgument, exception: null);
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
