using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace SynapseAdmin.Services;

public class UserService(IMatrixSessionService sessionService, ILogger<UserService> logger, IStringLocalizer<SharedResources> L) : IUserService
{
    private const int MaxMediaPageSize = 100;
    private const int MediaTotalsSearchLimit = 100;

    /// <summary>Uploads deleted per request: Synapse's default <c>limit</c>.</summary>
    internal const int MediaDeletionPageSize = 100;

    /// <summary>Requests per run (10,000 uploads); running it again continues (#599).</summary>
    internal const int MaxMediaDeletionRequests = 100;

    public Task<OperationResult<(int Total, List<UserListViewModel> Users)>> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingUserList",
            ex => logger.LogError(ex, "Error fetching user list (offset: {Offset}, limit: {Limit}, searchTerm: {SearchTerm})", offset, limit, searchTerm.SanitizeForLogging()),
            async gateway =>
            {
                var dir = direction.ToSynapseDir(whenUnsorted: "f");
                var result = await gateway.GetUserListAsync(offset, limit, orderBy, dir, searchTerm, token);
                if (result == null) return OperationResult<(int Total, List<UserListViewModel> Users)>.Ok((0, []));

                var vms = result.Users.ToViewModels();

                return OperationResult<(int Total, List<UserListViewModel> Users)>.Ok((result.Total, vms));
            },
            // A homeserver that can't sort by this key works as intended, and the dashboard asks for it by itself (#559).
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support sorting the user list by {OrderBy}", orderBy.SanitizeForLogging()));

    public Task<OperationResult<UserDetailViewModel>> GetUserDetailsAsync(string userId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingUserDetails",
            ex => logger.LogError(ex, "Error fetching user details for {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var serverName = gateway.ServerName;
                if (!string.IsNullOrEmpty(serverName) && !MatrixIdHelper.IsOnServer(userId, serverName))
                {
                    return OperationResult<UserDetailViewModel>.Failure(L["CannotQueryRemoteUser"], Severity.Warning);
                }

                var u = await gateway.GetUserDetailsAsync(userId, token);
                if (u == null) return OperationResult<UserDetailViewModel>.Failure(L["UserNotFound"]);


                async Task<OperationResult<List<UserMembershipViewModel>>> FetchMembershipsSafeAsync()
                {
                    try
                    {
                        return await GetUserMembershipsAsync(userId, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to fetch memberships for user {UserId}. Continuing without memberships.", userId.SanitizeForLogging());
                        return OperationResult<List<UserMembershipViewModel>>.Ok([]);
                    }
                }

                async Task<SynapseAdminExperimentalFeaturesResponse?> FetchExperimentalFeaturesSafeAsync()
                {
                    try
                    {
                        return await gateway.GetUserExperimentalFeaturesAsync(userId, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (OperationNotSupportedByHomeserverException)
                    {
                        // Not a failure: the homeserver has no per-user features, so the page shows none.
                        logger.LogDebug("The homeserver doesn't support experimental features for user {UserId}", userId.SanitizeForLogging());
                        return null;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to fetch experimental features for user {UserId}. Continuing without experimental features.", userId.SanitizeForLogging());
                        return null;
                    }
                }

                // Media is loaded page by page by the media table (GetUserMediaPageAsync), not here.
                var membershipsTask = FetchMembershipsSafeAsync();
                var experimentalFeaturesTask = FetchExperimentalFeaturesSafeAsync();

                await Task.WhenAll(membershipsTask, experimentalFeaturesTask);

                var membershipsResult = await membershipsTask;
                var experimentalFeaturesResult = await experimentalFeaturesTask;

                var vm = u.ToDetailViewModel();
                vm.Memberships = membershipsResult.Success ? (membershipsResult.Data ?? []) : [];
                vm.ExperimentalFeatures = experimentalFeaturesResult == null
                    ? null
                    : MapExperimentalFeatures(experimentalFeaturesResult.Features);

                return OperationResult<UserDetailViewModel>.Ok(vm);
            });

    public Task<OperationResult<UserMediaPageViewModel>> GetUserMediaPageAsync(string userId, int offset, int limit, CancellationToken token = default)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, MaxMediaPageSize);

        return sessionService.RunGatewayCallAsync(L, "ErrorFetchingMedia",
            ex => logger.LogError(ex, "Error fetching media page (offset {Offset}, limit {Limit}) for user {UserId}", offset, limit, userId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetUserMediaAsync(userId, offset, limit, orderBy: "created_ts", dir: "b", cancellationToken: token);
                var page = new UserMediaPageViewModel
                {
                    Offset = offset,
                    TotalCount = result?.Total ?? 0,
                    Items = (result?.Media ?? []).Select(m => m.ToViewModel()).ToList()
                };
                return OperationResult<UserMediaPageViewModel>.Ok(page);
            });
    }

    /// <summary>
    /// Search term for the user media statistics endpoint. Synapse filters with
    /// <c>user_id LIKE '@%' || term || '%:%'</c>, so a full Matrix ID can never match; the localpart must be used
    /// (it also matches on servers that use a plain substring search).
    /// </summary>
    internal static string MediaStatisticsSearchTerm(string userId)
    {
        var withoutSigil = userId.StartsWith('@') ? userId[1..] : userId;
        var colon = withoutSigil.IndexOf(':');
        return colon >= 0 ? withoutSigil[..colon] : withoutSigil;
    }

    public Task<OperationResult<UserMediaTotalsViewModel>> GetUserMediaTotalsAsync(string userId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingMedia",
            ex => logger.LogError(ex, "Error fetching media totals for user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                // Search by localpart, then pick the exact user: the term also matches similar IDs and display names.
                var stats = await gateway.GetUserMediaStatisticsAsync(MediaTotalsSearchLimit, searchTerm: MediaStatisticsSearchTerm(userId), cancellationToken: token);
                var users = stats?.Users ?? [];
                var match = users.FirstOrDefault(u => string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel { Count = match.MediaCount, Bytes = match.MediaLength });
                }

                // The statistics only list users with uploads. If every match was returned, the user has none.
                if (stats != null && stats.Total <= users.Count)
                {
                    return OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel());
                }

                logger.LogWarning("Could not determine media totals for user {UserId}: not among the first {Limit} statistics matches.", userId.SanitizeForLogging(), MediaTotalsSearchLimit);
                return OperationResult<UserMediaTotalsViewModel>.Failure(L["ErrorFetchingMedia"]);
            });

    public Task<OperationResult> DeactivateUserAsync(string userId, bool erase = false, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeactivatingUser",
            ex => logger.LogError(ex, "Error deactivating user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.DeactivateUserAsync(userId, erase, token);
                logger.LogInformation("Successfully deactivated user {UserId} (erase: {Erase})", userId.SanitizeForLogging(), erase);
                return OperationResult.Ok(L["UserDeactivatedSuccessfully"]);
            });

    public Task<OperationResult> QuarantineMediaAsync(string userId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorQuarantiningMedia",
            ex => logger.LogError(ex, "Error quarantining media for user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.QuarantineMediaByUserIdAsync(userId, token);
                logger.LogInformation("Successfully quarantined media for user {UserId}", userId.SanitizeForLogging());
                return OperationResult.Ok(L["UserMediaQuarantinedSuccessfully"]);
            });

    public Task<OperationResult<UserMediaDeletionViewModel>> DeleteAllMediaAsync(string userId, IProgress<int>? progress = null, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingUserMedia",
            ex => logger.LogError(ex, "Error deleting the media of user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var deleted = 0;
                var requests = 0;
                var capReached = false;
                while (true)
                {
                    SynapseAdminDeleteMediaResponse? response;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        response = await gateway.DeleteUserMediaAsync(userId, MediaDeletionPageSize, token);
                    }
                    // Before anything is deleted, a cancellation or failure gets the usual handling.
                    catch (OperationCanceledException) when (deleted > 0)
                    {
                        return MediaDeletionResult(userId, deleted, requests, null, UserMediaDeletionOutcome.Stopped);
                    }
                    catch (Exception ex) when (deleted > 0 && ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Error deleting the media of user {UserId} after {Deleted} uploads in {Requests} requests",
                            userId.SanitizeForLogging(), deleted, requests);
                        return MediaDeletionResult(userId, deleted, requests, null, UserMediaDeletionOutcome.Failed, logged: true);
                    }

                    requests++;
                    var count = response?.Total ?? 0;
                    if (count <= 0) break;

                    deleted += count;
                    progress?.Report(deleted);
                    if (requests >= MaxMediaDeletionRequests)
                    {
                        capReached = true;
                        break;
                    }
                }

                // A request that deletes nothing doesn't prove the list is empty: files the homeserver can't remove stay.
                long remaining;
                try
                {
                    var list = await gateway.GetUserMediaAsync(userId, 0, 1, "created_ts", "b", token);
                    remaining = list?.Total ?? 0;
                }
                catch (OperationCanceledException) when (deleted > 0)
                {
                    return MediaDeletionResult(userId, deleted, requests, null, UserMediaDeletionOutcome.Stopped);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not check the remaining media of user {UserId} after deleting {Deleted} uploads",
                        userId.SanitizeForLogging(), deleted);
                    return MediaDeletionResult(userId, deleted, requests, null, UserMediaDeletionOutcome.RemainingUnknown);
                }

                var outcome = remaining == 0 ? UserMediaDeletionOutcome.AllDeleted
                    : capReached ? UserMediaDeletionOutcome.CapReached
                    : UserMediaDeletionOutcome.SomeRemain;
                return MediaDeletionResult(userId, deleted, requests, remaining, outcome);
            });

    private OperationResult<UserMediaDeletionViewModel> MediaDeletionResult(string userId, int deleted, int requests, long? remaining,
        UserMediaDeletionOutcome outcome, bool logged = false)
    {
        if (!logged)
        {
            logger.LogInformation("Deleted {Deleted} uploads of user {UserId} in {Requests} requests (outcome: {Outcome}, remaining: {Remaining})",
                deleted, userId.SanitizeForLogging(), requests, outcome, remaining);
        }

        var data = new UserMediaDeletionViewModel { Deleted = deleted, Remaining = remaining, Outcome = outcome };
        var (message, severity) = outcome switch
        {
            UserMediaDeletionOutcome.AllDeleted => (L["UserMediaAllDeleted", deleted].Value, Severity.Success),
            UserMediaDeletionOutcome.SomeRemain => (L["UserMediaSomeRemain", deleted, remaining ?? 0].Value, Severity.Warning),
            UserMediaDeletionOutcome.CapReached => (L["UserMediaCapReached", deleted, remaining ?? 0].Value, Severity.Warning),
            UserMediaDeletionOutcome.Stopped => (L["UserMediaDeletionStopped", deleted].Value, Severity.Warning),
            UserMediaDeletionOutcome.Failed => (L["UserMediaDeletionFailed", deleted].Value, Severity.Error),
            _ => (L["UserMediaRemainingUnknown", deleted].Value, Severity.Warning)
        };
        return new OperationResult<UserMediaDeletionViewModel>
        {
            Success = outcome == UserMediaDeletionOutcome.AllDeleted,
            Message = message,
            Severity = severity,
            Data = data
        };
    }

    public Task<OperationResult<string>> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorLoggingInAsUser",
            ex => logger.LogError(ex, "Error performing shadow login for user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var resp = await gateway.LoginAsUserAsync(userId, expireIn, token);
                logger.LogInformation("Admin successfully performed shadow login as user {UserId}", userId.SanitizeForLogging());
                return OperationResult<string>.Ok(resp.AccessToken, L["ShadowLoginSuccessful", (int)expireIn.TotalMinutes]);
            });

    public Task<OperationResult> SendServerNoticeAsync(string userId, string message, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorSendingServerNotice",
            ex => logger.LogError(ex, "Error sending server notice to user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var content = new { msgtype = "m.text", body = message };
                await gateway.SendServerNoticeAsync(userId, content, cancellationToken: token);
                logger.LogInformation("Successfully sent server notice to user {UserId}", userId.SanitizeForLogging());
                return OperationResult.Ok(L["ServerNoticeSentSuccessfully"]);
            });

    public Task<OperationResult<List<UserMediaStatisticsViewModel>>> GetTopMediaUsersAsync(int count = 10, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingTopMediaUsers",
            ex => logger.LogError(ex, "Error fetching top media users"),
            async gateway =>
            {
                var result = await gateway.GetUserMediaStatisticsAsync(count, cancellationToken: token);
                if (result == null) return OperationResult<List<UserMediaStatisticsViewModel>>.Ok([]);

                var vms = result.Users.Select(u => u.ToViewModel()).ToList();

                return OperationResult<List<UserMediaStatisticsViewModel>>.Ok(vms);
            });

    public Task<OperationResult> CreateUserAsync(UserCreateViewModel model, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorCreatingUser",
            ex => logger.LogError(ex, "Error creating user {UserId}", model.UserId.SanitizeForLogging()),
            async gateway =>
            {
                // PUT /_synapse/admin/v2/users/{userId} creates the user or modifies an existing one: it would remove
                // an admin's rights, reactivate the account and replace the password. Only a user the homeserver
                // doesn't know is created.
                if (await gateway.GetUserDetailsAsync(model.UserId, token) != null)
                {
                    logger.LogWarning("Refused to create user {UserId}: the account already exists", model.UserId.SanitizeForLogging());
                    return OperationResult.Failure(L["UserAlreadyExists", model.UserId], Severity.Warning);
                }

                var req = new SynapseAdminUserUpsertRequest
                {
                    Password = model.Password,
                    DisplayName = model.DisplayName,
                    Admin = model.Admin,
                    Deactivated = model.Deactivated
                };

                var result = await gateway.UpdateUserAsync(model.UserId, req, token);

                if (result == null) return OperationResult.Failure(L["ErrorCreatingUser"]);

                logger.LogInformation("Successfully created user {UserId}", model.UserId.SanitizeForLogging());
                return OperationResult.Ok(L["UserCreatedSuccessfully"]);
            });

    public Task<OperationResult> SetUserLockedAsync(string userId, bool locked, CancellationToken token = default)
    {
        var failureKey = locked ? "ErrorLockingUser" : "ErrorUnlockingUser";
        return sessionService.RunGatewayCallAsync(L, failureKey,
            ex => logger.LogError(ex, "Error setting the lock state of user {UserId} (locked: {Locked})", userId.SanitizeForLogging(), locked),
            gateway => EditExistingUserAsync(gateway, userId, new SynapseAdminUserUpsertRequest { Locked = locked }, failureKey, token,
                () =>
                {
                    logger.LogInformation("Successfully set the lock state of user {UserId} (locked: {Locked})", userId.SanitizeForLogging(), locked);
                    return OperationResult.Ok(L[locked ? "UserLockedSuccessfully" : "UserUnlockedSuccessfully"]);
                }));
    }

    public Task<OperationResult> SetUserAdminAsync(string userId, bool admin, CancellationToken token = default)
    {
        var failureKey = admin ? "ErrorGrantingAdmin" : "ErrorRevokingAdmin";
        return sessionService.RunGatewayCallAsync(L, failureKey,
            ex => logger.LogError(ex, "Error setting the server admin state of user {UserId} (admin: {Admin})", userId.SanitizeForLogging(), admin),
            gateway => EditExistingUserAsync(gateway, userId, new SynapseAdminUserUpsertRequest { Admin = admin }, failureKey, token,
                () =>
                {
                    logger.LogInformation("Successfully set the server admin state of user {UserId} (admin: {Admin})", userId.SanitizeForLogging(), admin);
                    return OperationResult.Ok(L[admin ? "AdminGrantedSuccessfully" : "AdminRevokedSuccessfully"]);
                }));
    }

    /// <summary>
    /// Sends <paramref name="request"/> as the v2 user PUT, which creates an account it doesn't know, so only for an
    /// account the homeserver knows: the mirror of <see cref="CreateUserAsync"/>'s check (#584). The request carries
    /// only the fields the edit changes; the PUT leaves every omitted field, <c>admin</c> included, unchanged.
    /// </summary>
    private async Task<OperationResult> EditExistingUserAsync(IMatrixGateway gateway, string userId, SynapseAdminUserUpsertRequest request,
        string failureKey, CancellationToken token, Func<OperationResult> onSuccess,
        Func<SynapseAdminUserListResult.SynapseAdminUserListResultUser, OperationResult?>? refuse = null)
    {
        var current = await gateway.GetUserDetailsAsync(userId, token);
        if (current == null)
        {
            return OperationResult.Failure(L["UserNotFound"]);
        }

        if (refuse?.Invoke(current) is { } refusal)
        {
            return refusal;
        }

        var result = await gateway.UpdateUserAsync(userId, request, token);
        return result == null ? OperationResult.Failure(L[failureKey]) : onSuccess();
    }

    public Task<OperationResult> ResetPasswordAsync(string userId, string newPassword, bool logoutDevices, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorResettingPassword",
            ex => logger.LogError(ex, "Error resetting the password of user {UserId} (logout devices: {LogoutDevices})", userId.SanitizeForLogging(), logoutDevices),
            async gateway =>
            {
                await gateway.ResetPasswordAsync(userId, new SynapseAdminResetPasswordRequest { NewPassword = newPassword, LogoutDevices = logoutDevices }, token);
                logger.LogInformation("Successfully reset the password of user {UserId} (logout devices: {LogoutDevices})", userId.SanitizeForLogging(), logoutDevices);
                return OperationResult.Ok(L["PasswordResetSuccessfully"]);
            });

    public Task<OperationResult> ReactivateUserAsync(string userId, string? newPassword, CancellationToken token = default)
    {
        var password = string.IsNullOrEmpty(newPassword) ? null : newPassword;
        var withPassword = password != null;
        return sessionService.RunGatewayCallAsync(L, "ErrorReactivatingUser",
            ex => logger.LogError(ex, "Error reactivating user {UserId} (with password: {WithPassword})", userId.SanitizeForLogging(), withPassword),
            gateway => EditExistingUserAsync(gateway, userId, new SynapseAdminUserUpsertRequest { Deactivated = false, Password = password },
                "ErrorReactivatingUser", token,
                () =>
                {
                    logger.LogInformation("Successfully reactivated user {UserId} (with password: {WithPassword})", userId.SanitizeForLogging(), withPassword);
                    return OperationResult.Ok(L["UserReactivatedSuccessfully"]);
                },
                // An account active again (reactivated elsewhere) would get a new password and, as logout_devices
                // defaults to true, lose all its sessions.
                current =>
                {
                    if (current.Deactivated) return null;
                    logger.LogWarning("Refused to reactivate user {UserId}: the account is active", userId.SanitizeForLogging());
                    return OperationResult.Failure(L["UserAlreadyActive"], Severity.Warning);
                }));
    }

    public Task<OperationResult> UpdateUserProfileAsync(string userId, UserProfileEditViewModel changes, CancellationToken token = default)
    {
        if (!changes.HasChanges)
        {
            return Task.FromResult(OperationResult.Ok(L["UserProfileUnchanged"]));
        }

        // The display name itself isn't logged, only which fields the edit changes.
        var displayNameChanged = changes.DisplayName != null;
        var request = new SynapseAdminUserUpsertRequest
        {
            DisplayName = changes.DisplayName,
            AvatarUrl = changes.RemoveAvatar ? "" : null
        };
        return sessionService.RunGatewayCallAsync(L, "ErrorUpdatingUserProfile",
            ex => logger.LogError(ex, "Error updating the profile of user {UserId} (display name: {DisplayNameChanged}, remove avatar: {RemoveAvatar})",
                userId.SanitizeForLogging(), displayNameChanged, changes.RemoveAvatar),
            gateway => EditExistingUserAsync(gateway, userId, request, "ErrorUpdatingUserProfile", token,
                () =>
                {
                    logger.LogInformation("Successfully updated the profile of user {UserId} (display name: {DisplayNameChanged}, remove avatar: {RemoveAvatar})",
                        userId.SanitizeForLogging(), displayNameChanged, changes.RemoveAvatar);
                    return OperationResult.Ok(L["UserProfileUpdatedSuccessfully"]);
                }));
    }

    public Task<OperationResult> SetUserSuspendedAsync(string userId, bool suspended, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, suspended ? "ErrorSuspendingUser" : "ErrorUnsuspendingUser",
            ex => logger.LogError(ex, "Error setting the suspension of user {UserId} (suspended: {Suspended})", userId.SanitizeForLogging(), suspended),
            async gateway =>
            {
                await gateway.SetUserSuspendedAsync(userId, suspended, token);
                logger.LogInformation("Successfully set the suspension of user {UserId} (suspended: {Suspended})", userId.SanitizeForLogging(), suspended);
                return OperationResult.Ok(L[suspended ? "UserSuspendedSuccessfully" : "UserUnsuspendedSuccessfully"]);
            });

    public Task<OperationResult> SetUserShadowBannedAsync(string userId, bool shadowBanned, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, shadowBanned ? "ErrorShadowBanningUser" : "ErrorLiftingShadowBan",
            ex => logger.LogError(ex, "Error setting the shadow-ban of user {UserId} (shadow-banned: {ShadowBanned})", userId.SanitizeForLogging(), shadowBanned),
            async gateway =>
            {
                await gateway.SetUserShadowBannedAsync(userId, shadowBanned, token);
                logger.LogInformation("Successfully set the shadow-ban of user {UserId} (shadow-banned: {ShadowBanned})", userId.SanitizeForLogging(), shadowBanned);
                return OperationResult.Ok(L[shadowBanned ? "UserShadowBannedSuccessfully" : "ShadowBanLiftedSuccessfully"]);
            });

    public Task<OperationResult<List<UserMembershipViewModel>>> GetUserMembershipsAsync(string userId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingUserMemberships",
            ex => logger.LogError(ex, "Error fetching memberships for user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetUserMembershipsAsync(userId, token);

                if (result == null) return OperationResult<List<UserMembershipViewModel>>.Ok([]);

                var vms = result.ToViewModels();

                return OperationResult<List<UserMembershipViewModel>>.Ok(vms);
            });

    public Task<OperationResult<List<UserExperimentalFeatureViewModel>>> GetUserExperimentalFeaturesAsync(string userId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingExperimentalFeatures",
            ex => logger.LogError(ex, "Error fetching experimental features for user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var result = await gateway.GetUserExperimentalFeaturesAsync(userId, token);
                if (result == null) return OperationResult<List<UserExperimentalFeatureViewModel>>.Ok([]);

                return OperationResult<List<UserExperimentalFeatureViewModel>>.Ok(MapExperimentalFeatures(result.Features));
            },
            // A page loads this by itself: a homeserver that refuses it works as intended, so the refusal isn't an error.
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support experimental features for user {UserId}", userId.SanitizeForLogging()));

    public Task<OperationResult> SetUserExperimentalFeaturesAsync(string userId, Dictionary<string, bool> features, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorUpdatingExperimentalFeatures",
            ex => logger.LogError(ex, "Error updating experimental features for user {UserId}", userId.SanitizeForLogging()),
            async gateway =>
            {
                var req = new SynapseAdminExperimentalFeaturesRequest
                {
                    Features = features
                };
                await gateway.SetUserExperimentalFeaturesAsync(userId, req, token);
                logger.LogInformation("Successfully updated experimental features for user {UserId}", userId.SanitizeForLogging());
                return OperationResult.Ok(L["ExperimentalFeaturesUpdatedSuccessfully"]);
            });

    public Task<OperationResult<long>> RenewAccountValidityAsync(AccountValidityRenewViewModel model, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorRenewingAccountValidity",
            ex => logger.LogError(ex, "Error renewing account validity for user {UserId}", model.UserId.SanitizeForLogging()),
            async gateway =>
            {
                var req = new SynapseAdminAccountValidityRequest
                {
                    UserId = model.UserId,
                    ExpirationTs = model.ExpirationTs,
                    EnableRenewalEmails = model.EnableRenewalEmails
                };

                var resp = await gateway.RenewAccountValidityAsync(req, token);
                if (resp == null) return OperationResult<long>.Failure(L["ErrorRenewingAccountValidity"]);

                logger.LogInformation("Successfully renewed account validity for user {UserId} (new expiration_ts: {ExpirationTs})", model.UserId.SanitizeForLogging(), resp.ExpirationTs);
                return OperationResult<long>.Ok(resp.ExpirationTs, L["AccountValidityRenewedSuccessfully"]);
            });

    private List<UserExperimentalFeatureViewModel> MapExperimentalFeatures(Dictionary<string, bool> features)
    {
        return features
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
            {
                var resourceKey = $"ExperimentalFeature_{kvp.Key.ToLowerInvariant()}";
                var localized = L[resourceKey];
                var description = localized.ResourceNotFound ? L["ExperimentalFeature_Unknown"].Value : localized.Value;

                return new UserExperimentalFeatureViewModel
                {
                    FeatureKey = kvp.Key,
                    Description = description,
                    Enabled = kvp.Value
                };
            })
            .ToList();
    }
}
