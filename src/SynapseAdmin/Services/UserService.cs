using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.EventTypes.Spec;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Extensions;
using MudBlazor;

namespace SynapseAdmin.Services;

public class UserService(MatrixSessionService sessionService)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<(int Total, List<UserListViewModel> Users)> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return (0, []);

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
            CreationTs = u.CreationTs / 1000, // SDK is in ms, VM uses seconds for DateTime helper
            UserType = u.UserType ?? "user",
            Locked = u.Locked,
            IsGuest = u.IsGuest == true
        }).ToList();

        return (result.Total, vms);
    }

    public async Task<UserDetailViewModel?> GetUserDetailsAsync(string userId)
    {
        if (SynapseAdmin == null) return null;

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
            // These don't exist in the SDK model directly, setting to empty/default if needed or removing from VM
            ConsentVersion = "", 
            ConsentServerNoticeSent = "",
            AppserviceId = "",
            Media = mediaResult == null ? null : new UserMediaViewModel
            {
                TotalCount = mediaResult.Total,
                TotalSize = 0, // Not available in this SDK model
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

    public async Task DeactivateUserAsync(string userId, bool erase = false)
    {
        if (SynapseAdmin == null) return;

        var encodedUserId = Uri.EscapeDataString(userId);
        var payload = new { erase };
        await SynapseAdmin.ClientHttpClient.PostAsJsonAsync($"/_synapse/admin/v1/deactivate/{encodedUserId}", payload);
    }

    public async Task QuarantineMediaAsync(string userId)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.QuarantineMediaByUserId(userId);
    }

    public async Task<string?> LoginAsUserAsync(string userId, TimeSpan expireIn)
    {
        if (SynapseAdmin == null) return null;
        // The LibMatrix SDK call
        var resp = await SynapseAdmin.Admin.LoginUserAsync(userId, expireIn);
        return resp.AccessToken;
    }

    public async Task SendServerNoticeAsync(string userId, string message)
    {
        if (SynapseAdmin == null) return;
        var content = new RoomMessageEventContent(body: message);
        await SynapseAdmin.SendServerNoticeAsync(userId, content);
    }
}
