using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.EventTypes.Spec;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Extensions;
using MudBlazor;

namespace SynapseAdmin.Services;

public class UserService(MatrixSessionService sessionService, ILogger<UserService> logger)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<(int Total, List<UserListViewModel> Users)> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return (0, []);

        try
        {
            var dir = direction == SortDirection.Descending ? "b" : "f";
            var url = $"/_synapse/admin/v2/users?from={offset}&limit={limit}&dir={dir}&order_by={orderBy}";

            var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult>(url, cancellationToken: token);
            if (result == null) return (0, []);
            
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

            return (result.Total, vms);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user list (offset: {Offset}, limit: {Limit})", offset, limit);
            throw;
        }
    }

    public async Task<UserDetailViewModel?> GetUserDetailsAsync(string userId)
    {
        if (SynapseAdmin == null) return null;

        try
        {
            var encodedUserId = Uri.EscapeDataString(userId);
            var u = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult.SynapseAdminUserListResultUser>($"/_synapse/admin/v2/users/{encodedUserId}");
            
            if (u == null) return null;

            var mediaResult = await SynapseAdmin.Admin.GetUserMediaAsync(userId);

            return new UserDetailViewModel
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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching user details for {UserId}", userId);
            throw;
        }
    }

    public async Task DeactivateUserAsync(string userId, bool erase = false)
    {
        if (SynapseAdmin == null) return;

        try
        {
            await SynapseAdmin.Admin.DeactivateUserAsync(userId, erase);
            logger.LogInformation("Successfully deactivated user {UserId} (erase: {Erase})", userId, erase);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deactivating user {UserId}", userId);
            throw;
        }
    }

    public async Task QuarantineMediaAsync(string userId)
    {
        if (SynapseAdmin == null) return;
        try
        {
            await SynapseAdmin.Admin.QuarantineMediaByUserId(userId);
            logger.LogInformation("Successfully quarantined media for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error quarantining media for user {UserId}", userId);
            throw;
        }
    }

    public async Task<string?> LoginAsUserAsync(string userId, TimeSpan expireIn)
    {
        if (SynapseAdmin == null) return null;
        try
        {
            var resp = await SynapseAdmin.Admin.LoginUserAsync(userId, expireIn);
            logger.LogInformation("Admin successfully performed shadow login as user {UserId}", userId);
            return resp.AccessToken;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing shadow login for user {UserId}", userId);
            throw;
        }
    }

    public async Task SendServerNoticeAsync(string userId, string message)
    {
        if (SynapseAdmin == null) return;
        try
        {
            var content = new RoomMessageEventContent(body: message);
            await SynapseAdmin.SendServerNoticeAsync(userId, content);
            logger.LogInformation("Successfully sent server notice to user {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending server notice to user {UserId}", userId);
            throw;
        }
    }
}
