using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.EventTypes.Spec;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;

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
                CreationTs = u.CreationTs / 1000, 
                UserType = u.UserType ?? "user",
                Locked = u.Locked,
                IsGuest = u.IsGuest == true
            }).ToList();

            return OperationResult<(int Total, List<UserListViewModel> Users)>.Ok((result.Total, vms));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user list (offset: {Offset}, limit: {Limit})", offset, limit);
            return OperationResult<(int Total, List<UserListViewModel> Users)>.Failure(string.Format(L["ErrorFetchingUserList"], ex.Message));
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

            var mediaResult = await SynapseAdmin.Admin.GetUserMediaAsync(userId);

            var vm = new UserDetailViewModel
            {
                UserId = u.Name,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Deactivated = u.Deactivated,
                Admin = u.Admin == true,
                CreationTs = u.CreationTs / 1000,
                UserType = u.UserType ?? "user",
                Locked = u.Locked,
                ShadowBanned = u.ShadowBanned,
                ConsentVersion = "", 
                ConsentServerNoticeSent = "",
                AppserviceId = "",
                Media = mediaResult == null ? null : new UserMediaViewModel
                {
                    TotalCount = mediaResult.Total,
                    TotalSize = 0, 
                    Media = mediaResult.Media.Select(m => new UserMediaItemViewModel
                    {
                        MediaId = m.MediaId,
                        UploadName = m.UploadName,
                        MediaLength = m.MediaLength,
                        CreatedTimestamp = m.CreatedTimestamp
                    }).ToList()
                }
            };
            return OperationResult<UserDetailViewModel>.Ok(vm);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user details for {UserId}", userId);
            return OperationResult<UserDetailViewModel>.Failure(string.Format(L["ErrorFetchingUserDetails"], ex.Message));
        }
    }

    public async Task<OperationResult> DeactivateUserAsync(string userId, bool erase = false)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            await SynapseAdmin.Admin.DeactivateUserAsync(userId, erase);
            logger.LogInformation("Successfully deactivated user {UserId} (erase: {Erase})", userId, erase);
            return OperationResult.Ok(L["UserDeactivatedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deactivating user {UserId}", userId);
            return OperationResult.Failure(string.Format(L["ErrorDeactivatingUser"], ex.Message));
        }
    }

    public async Task<OperationResult> QuarantineMediaAsync(string userId)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await SynapseAdmin.Admin.QuarantineMediaByUserId(userId);
            logger.LogInformation("Successfully quarantined media for user {UserId}", userId);
            return OperationResult.Ok(L["UserMediaQuarantinedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error quarantining media for user {UserId}", userId);
            return OperationResult.Failure(string.Format(L["ErrorQuarantiningMedia"], ex.Message));
        }
    }

    public async Task<OperationResult<string>> LoginAsUserAsync(string userId, TimeSpan expireIn)
    {
        if (SynapseAdmin == null) return OperationResult<string>.Failure(L["NotAuthenticated"]);
        try
        {
            var resp = await SynapseAdmin.Admin.LoginUserAsync(userId, expireIn);
            logger.LogInformation("Admin successfully performed shadow login as user {UserId}", userId);
            return OperationResult<string>.Ok(resp.AccessToken, string.Format(L["ShadowLoginSuccessful"], userId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing shadow login for user {UserId}", userId);
            return OperationResult<string>.Failure(string.Format(L["ErrorLoggingInAsUser"], ex.Message));
        }
    }

    public async Task<OperationResult> SendServerNoticeAsync(string userId, string message)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            var content = new RoomMessageEventContent(body: message);
            await SynapseAdmin.SendServerNoticeAsync(userId, content);
            logger.LogInformation("Successfully sent server notice to user {UserId}", userId);
            return OperationResult.Ok(L["ServerNoticeSentSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending server notice to user {UserId}", userId);
            return OperationResult.Failure(string.Format(L["ErrorSendingServerNotice"], ex.Message));
        }
    }
}
