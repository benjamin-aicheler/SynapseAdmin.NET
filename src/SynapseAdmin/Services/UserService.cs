using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
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

namespace SynapseAdmin.Services;

public class UserService(IMatrixSessionService sessionService, ILogger<UserService> logger, IStringLocalizer<SharedResources> L) : IUserService
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<OperationResult<(int Total, List<UserListViewModel> Users)>> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return OperationResult<(int Total, List<UserListViewModel> Users)>.Failure(L["NotAuthenticated"]);

        try
        {
            var dir = direction == SortDirection.Descending ? "b" : "f";
            var url = $"/_synapse/admin/v2/users?from={offset}&limit={limit}&dir={dir}&order_by={orderBy}";

            var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult>(url, cancellationToken: token);
            if (result == null) return OperationResult<(int Total, List<UserListViewModel> Users)>.Ok((0, []));
            
            var vms = result.Users.Select(u => new UserListViewModel
            {
                UserId = u.Name,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Deactivated = u.Deactivated,
                Admin = u.Admin == true,
                CreationTs = DateTimeOffset.FromUnixTimeMilliseconds(u.CreationTs).ToUnixTimeSeconds(), 
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
        if (SynapseAdmin == null) return OperationResult<UserDetailViewModel>.Failure(L["NotAuthenticated"]);

        try
        {
            var encodedUserId = Uri.EscapeDataString(userId);
            var u = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>($"/_synapse/admin/v2/users/{encodedUserId}");
            
            if (u == null) return OperationResult<UserDetailViewModel>.Failure(L["UserNotFound"]);

            var mediaTask = SynapseAdmin.Admin.GetUserMediaAsync(userId);
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
                CreationTs = DateTimeOffset.FromUnixTimeMilliseconds(u.CreationTs).ToUnixTimeSeconds(),
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
                        MediaLength = m.MediaLength,
                        CreatedTimestamp = m.CreatedTimestamp
                    }).ToList()
                },
                Memberships = membershipsResult.Success ? (membershipsResult.Data ?? []) : []
            };
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
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            await SynapseAdmin.Admin.DeactivateUserAsync(userId, erase);
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
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await SynapseAdmin.Admin.QuarantineMediaByUserId(userId);
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
        if (SynapseAdmin == null) return OperationResult<string>.Failure(L["NotAuthenticated"]);
        try
        {
            var resp = await SynapseAdmin.Admin.LoginUserAsync(userId, expireIn);
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
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            var content = new RoomMessageEventContent(body: message);
            await SynapseAdmin.SendServerNoticeAsync(userId, content);
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
        if (SynapseAdmin == null) return OperationResult<List<UserMediaStatisticsViewModel>>.Failure(L["NotAuthenticated"]);
        try
        {
            var result = await SynapseAdmin.GetUserMediaStatisticsAsync(count);
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
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            var encodedUserId = Uri.EscapeDataString(model.UserId);
            var req = new SynapseAdminUserUpsertRequest
            {
                Password = model.Password,
                DisplayName = model.DisplayName,
                Admin = model.Admin,
                Deactivated = model.Deactivated
            };

            var response = await SynapseAdmin.ClientHttpClient.PutAsJsonAsync($"/_synapse/admin/v2/users/{encodedUserId}", req);
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
        if (SynapseAdmin == null) return OperationResult<List<UserMembershipViewModel>>.Failure(L["NotAuthenticated"]);

        try
        {
            var encodedUserId = Uri.EscapeDataString(userId);
            var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserMembershipsResponse>($"/_synapse/admin/v1/users/{encodedUserId}/memberships");
            
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
