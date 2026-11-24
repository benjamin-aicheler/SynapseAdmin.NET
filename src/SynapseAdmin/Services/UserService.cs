using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace SynapseAdmin.Services;

public class UserService(IMatrixSessionService sessionService, ILogger<UserService> logger, IStringLocalizer<SharedResources> L) : IUserService
{
    private const int MaxMediaPageSize = 100;
    private const int MediaTotalsSearchLimit = 100;

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
            });

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
            });

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
