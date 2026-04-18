using LibMatrix.EventTypes.Spec;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using System.Net.Http.Json;
using SynapseAdmin.Interfaces.Gateways;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;

namespace SynapseAdmin.Services;

public class UserService(IMatrixSessionService sessionService, ILogger<UserService> logger, IStringLocalizer<SharedResources> L) : IUserService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<(int Total, List<UserListViewModel> Users)>> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<(int Total, List<UserListViewModel> Users)>.Failure(L["NotAuthenticated"]);

        try
        {
            var dir = direction == SortDirection.Descending ? "b" : "f";
            var result = await Gateway.GetUserListAsync(offset, limit, orderBy, dir, token);
            if (result == null) return OperationResult<(int Total, List<UserListViewModel> Users)>.Ok((0, []));
            
            var vms = result.Users.Select(u => new UserListViewModel
            {
                UserId = u.Name,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Deactivated = u.Deactivated,
                Admin = u.Admin == true,
                CreationTs = u.CreationTs, 
                UserType = u.UserType ?? "user",
                Locked = u.Locked,
                IsGuest = u.IsGuest == true
            }).ToList();

            return OperationResult<(int Total, List<UserListViewModel> Users)>.Ok((result.Total, vms));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user list (offset: {Offset}, limit: {Limit})", offset, limit);
            return OperationResult<(int Total, List<UserListViewModel> Users)>.Failure(L["ErrorFetchingUserList"]);
        }
    }

    public async Task<OperationResult<UserDetailViewModel>> GetUserDetailsAsync(string userId)
    {
        if (Gateway == null) return OperationResult<UserDetailViewModel>.Failure(L["NotAuthenticated"]);

        try
        {
            var u = await Gateway.GetUserDetailsAsync(userId);
            if (u == null) return OperationResult<UserDetailViewModel>.Failure(L["UserNotFound"]);


            var mediaTask = Gateway.GetUserMediaAsync(userId);
            var membershipsTask = GetUserMembershipsAsync(userId);

            await Task.WhenAll(mediaTask, membershipsTask);

            var mediaResult = await mediaTask;
            var membershipsResult = await membershipsTask;

            var vm = new UserDetailViewModel
            {
                UserId = u.Name,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Deactivated = u.Deactivated,
                Admin = u.Admin == true,
                CreationTs = u.CreationTs * 1000, // Converting seconds to milliseconds as expected by the ViewModel
                UserType = u.UserType ?? "user",
                Locked = u.Locked,
                ShadowBanned = u.ShadowBanned,
                ConsentVersion = "", 
                ConsentServerNoticeSent = "",
                AppserviceId = "",
                Media = mediaResult == null ? null : new UserMediaViewModel
                {
                    TotalCount = mediaResult.Total,
                    TotalSize = mediaResult.Media.Sum(m => m.MediaLength), 
                    Media = mediaResult.Media.Select(m => new UserMediaItemViewModel
                    {
                        MediaId = m.MediaId,
                        UploadName = m.UploadName,
                        MediaType = m.MediaType,
                        MediaLength = m.MediaLength,
                        CreatedTimestamp = m.CreatedTimestamp
                    }).ToList()
                },
                Memberships = membershipsResult.Success ? (membershipsResult.Data ?? []) : []
            };

            // Fetch avatar data if available and not too large (3MB limit)
            if (!string.IsNullOrEmpty(vm.AvatarUrl))
            {
                try
                {
                    vm.AvatarData = await Gateway.DownloadMediaAsync(vm.AvatarUrl);
                    if (vm.AvatarData == null)
                    {
                        logger.LogWarning("Avatar for user {UserId} is too large or failed to download, skipping embed", userId.SanitizeForLogging());
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch avatar data for user {UserId}", userId.SanitizeForLogging());
                }
            }

            return OperationResult<UserDetailViewModel>.Ok(vm);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user details for {UserId}", userId.SanitizeForLogging());
            return OperationResult<UserDetailViewModel>.Failure(L["ErrorFetchingUserDetails"]);
        }
    }

    public async Task<OperationResult> DeactivateUserAsync(string userId, bool erase = false)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            await Gateway.DeactivateUserAsync(userId, erase);
            logger.LogInformation("Successfully deactivated user {UserId} (erase: {Erase})", userId.SanitizeForLogging(), erase);
            return OperationResult.Ok(L["UserDeactivatedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deactivating user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorDeactivatingUser"]);
        }
    }

    public async Task<OperationResult> QuarantineMediaAsync(string userId)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await Gateway.QuarantineMediaByUserIdAsync(userId);
            logger.LogInformation("Successfully quarantined media for user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Ok(L["UserMediaQuarantinedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error quarantining media for user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorQuarantiningMedia"]);
        }
    }

    public async Task<OperationResult<string>> LoginAsUserAsync(string userId, TimeSpan expireIn)
    {
        if (Gateway == null) return OperationResult<string>.Failure(L["NotAuthenticated"]);
        try
        {
            var resp = await Gateway.LoginAsUserAsync(userId, expireIn);
            logger.LogInformation("Admin successfully performed shadow login as user {UserId}", userId.SanitizeForLogging());
            return OperationResult<string>.Ok(resp.AccessToken, L["ShadowLoginSuccessful"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing shadow login for user {UserId}", userId.SanitizeForLogging());
            return OperationResult<string>.Failure(L["ErrorLoggingInAsUser"]);
        }
    }

    public async Task<OperationResult> SendServerNoticeAsync(string userId, string message)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            var content = new RoomMessageEventContent(body: message);
            await Gateway.SendServerNoticeAsync(userId, content);
            logger.LogInformation("Successfully sent server notice to user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Ok(L["ServerNoticeSentSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending server notice to user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorSendingServerNotice"]);
        }
    }

    public async Task<OperationResult<List<UserMediaStatisticsViewModel>>> GetTopMediaUsersAsync(int count = 10)
    {
        if (Gateway == null) return OperationResult<List<UserMediaStatisticsViewModel>>.Failure(L["NotAuthenticated"]);
        try
        {
            var result = await Gateway.GetUserMediaStatisticsAsync(count);
            if (result == null) return OperationResult<List<UserMediaStatisticsViewModel>>.Ok([]);

            var vms = result.Users.Select(u => new UserMediaStatisticsViewModel
            {
                UserId = u.UserId,
                DisplayName = u.DisplayName,
                MediaCount = u.MediaCount,
                TotalSize = u.MediaLength
            }).ToList();

            return OperationResult<List<UserMediaStatisticsViewModel>>.Ok(vms);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching top media users");
            return OperationResult<List<UserMediaStatisticsViewModel>>.Failure(L["ErrorFetchingTopMediaUsers"]);
        }
    }

    public async Task<OperationResult> CreateUserAsync(UserCreateViewModel model)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            var req = new SynapseAdminUserUpsertRequest
            {
                Password = model.Password,
                DisplayName = model.DisplayName,
                Admin = model.Admin,
                Deactivated = model.Deactivated
            };

            var response = await Gateway.UpdateUserAsync(model.UserId, req);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>();
            
            if (result == null) return OperationResult.Failure(L["ErrorCreatingUser"]);

            logger.LogInformation("Successfully created user {UserId}", model.UserId.SanitizeForLogging());
            return OperationResult.Ok(L["UserCreatedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating user {UserId}", model.UserId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorCreatingUser"]);
        }
    }

    public async Task<OperationResult<List<UserMembershipViewModel>>> GetUserMembershipsAsync(string userId)
    {
        if (Gateway == null) return OperationResult<List<UserMembershipViewModel>>.Failure(L["NotAuthenticated"]);

        try
        {
            var result = await Gateway.GetUserMembershipsAsync(userId);
            
            if (result == null) return OperationResult<List<UserMembershipViewModel>>.Ok([]);

            var vms = result.Memberships.Select(kvp => new UserMembershipViewModel
            {
                RoomId = kvp.Key,
                Membership = kvp.Value
            }).ToList();

            return OperationResult<List<UserMembershipViewModel>>.Ok(vms);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching memberships for user {UserId}", userId.SanitizeForLogging());
            return OperationResult<List<UserMembershipViewModel>>.Failure(L["ErrorFetchingUserMemberships"]);
        }
    }
}
