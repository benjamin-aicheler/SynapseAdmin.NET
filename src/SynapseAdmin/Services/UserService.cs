using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Interfaces.Gateways;

namespace SynapseAdmin.Services;

public class UserService(IMatrixSessionService sessionService, ILogger<UserService> logger, IStringLocalizer<SharedResources> L) : IUserService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<(int Total, List<UserListViewModel> Users)>> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<(int Total, List<UserListViewModel> Users)>.Failure(L["NotAuthenticated"]);

        try
        {
            var dir = direction == SortDirection.Descending ? "b" : "f";
            var result = await Gateway.GetUserListAsync(offset, limit, orderBy, dir, searchTerm, token);
            if (result == null) return OperationResult<(int Total, List<UserListViewModel> Users)>.Ok((0, []));
            
            var vms = result.Users.ToViewModels();

            return OperationResult<(int Total, List<UserListViewModel> Users)>.Ok((result.Total, vms));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<(int Total, List<UserListViewModel> Users)>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user list (offset: {Offset}, limit: {Limit}, searchTerm: {SearchTerm})", offset, limit, searchTerm.SanitizeForLogging());
            return OperationResult<(int Total, List<UserListViewModel> Users)>.Failure(L["ErrorFetchingUserList"]);
        }
    }

    public async Task<OperationResult<UserDetailViewModel>> GetUserDetailsAsync(string userId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<UserDetailViewModel>.Failure(L["NotAuthenticated"]);

        try
        {
            var u = await Gateway.GetUserDetailsAsync(userId, token);
            if (u == null) return OperationResult<UserDetailViewModel>.Failure(L["UserNotFound"]);


            var mediaTask = Gateway.GetUserMediaAsync(userId, token);
            var membershipsTask = GetUserMembershipsAsync(userId, token);

            await Task.WhenAll(mediaTask, membershipsTask);

            var mediaResult = await mediaTask;
            var membershipsResult = await membershipsTask;

            var vm = u.ToDetailViewModel();
            vm.Media = mediaResult == null ? null : new UserMediaViewModel
            {
                TotalCount = mediaResult.Total,
                TotalSize = mediaResult.Media.Sum(m => m.MediaLength), 
                Media = mediaResult.Media.Select(m => new UserMediaItemViewModel
                {
                    MediaId = m.MediaId,
                    UploadName = m.UploadName,
                    MediaType = m.MediaType,
                    MediaLength = m.MediaLength,
                    CreatedTimestamp = m.CreatedTimestamp,
                    QuarantinedBy = m.QuarantinedBy,
                    SafeFromQuarantine = m.SafeFromQuarantine
                }).ToList()
            };
            vm.Memberships = membershipsResult.Success ? (membershipsResult.Data ?? []) : [];

            return OperationResult<UserDetailViewModel>.Ok(vm);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<UserDetailViewModel>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user details for {UserId}", userId.SanitizeForLogging());
            return OperationResult<UserDetailViewModel>.Failure(L["ErrorFetchingUserDetails"]);
        }
    }

    public async Task<OperationResult> DeactivateUserAsync(string userId, bool erase = false, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            await Gateway.DeactivateUserAsync(userId, erase, token);
            logger.LogInformation("Successfully deactivated user {UserId} (erase: {Erase})", userId.SanitizeForLogging(), erase);
            return OperationResult.Ok(L["UserDeactivatedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deactivating user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorDeactivatingUser"]);
        }
    }

    public async Task<OperationResult> QuarantineMediaAsync(string userId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await Gateway.QuarantineMediaByUserIdAsync(userId, token);
            logger.LogInformation("Successfully quarantined media for user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Ok(L["UserMediaQuarantinedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error quarantining media for user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorQuarantiningMedia"]);
        }
    }

    public async Task<OperationResult<string>> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<string>.Failure(L["NotAuthenticated"]);
        try
        {
            var resp = await Gateway.LoginAsUserAsync(userId, expireIn, token);
            logger.LogInformation("Admin successfully performed shadow login as user {UserId}", userId.SanitizeForLogging());
            return OperationResult<string>.Ok(resp.AccessToken, L["ShadowLoginSuccessful"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing shadow login for user {UserId}", userId.SanitizeForLogging());
            return OperationResult<string>.Failure(L["ErrorLoggingInAsUser"]);
        }
    }

    public async Task<OperationResult> SendServerNoticeAsync(string userId, string message, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            var content = new { msgtype = "m.text", body = message };
            await Gateway.SendServerNoticeAsync(userId, content, cancellationToken: token);
            logger.LogInformation("Successfully sent server notice to user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Ok(L["ServerNoticeSentSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending server notice to user {UserId}", userId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorSendingServerNotice"]);
        }
    }

    public async Task<OperationResult<List<UserMediaStatisticsViewModel>>> GetTopMediaUsersAsync(int count = 10, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<List<UserMediaStatisticsViewModel>>.Failure(L["NotAuthenticated"]);
        try
        {
            var result = await Gateway.GetUserMediaStatisticsAsync(count, cancellationToken: token);
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
        catch (OperationCanceledException)
        {
            return OperationResult<List<UserMediaStatisticsViewModel>>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching top media users");
            return OperationResult<List<UserMediaStatisticsViewModel>>.Failure(L["ErrorFetchingTopMediaUsers"]);
        }
    }

    public async Task<OperationResult> CreateUserAsync(UserCreateViewModel model, CancellationToken token = default)
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

            var result = await Gateway.UpdateUserAsync(model.UserId, req, token);
            
            if (result == null) return OperationResult.Failure(L["ErrorCreatingUser"]);

            logger.LogInformation("Successfully created user {UserId}", model.UserId.SanitizeForLogging());
            return OperationResult.Ok(L["UserCreatedSuccessfully"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating user {UserId}", model.UserId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorCreatingUser"]);
        }
    }

    public async Task<OperationResult<List<UserMembershipViewModel>>> GetUserMembershipsAsync(string userId, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<List<UserMembershipViewModel>>.Failure(L["NotAuthenticated"]);

        try
        {
            var result = await Gateway.GetUserMembershipsAsync(userId, token);
            
            if (result == null) return OperationResult<List<UserMembershipViewModel>>.Ok([]);

            var vms = result.Memberships.Select(kvp => new UserMembershipViewModel
            {
                RoomId = kvp.Key,
                Membership = kvp.Value
            }).ToList();

            return OperationResult<List<UserMembershipViewModel>>.Ok(vms);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<List<UserMembershipViewModel>>.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching memberships for user {UserId}", userId.SanitizeForLogging());
            return OperationResult<List<UserMembershipViewModel>>.Failure(L["ErrorFetchingUserMemberships"]);
        }
    }
}
