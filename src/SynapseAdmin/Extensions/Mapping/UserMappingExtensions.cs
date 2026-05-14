using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Extensions.Mapping;

public static class UserMappingExtensions
{
    public static UserListViewModel ToViewModel(this SynapseAdminUserListResult.SynapseAdminUserListResultUser user)
    {
        return new UserListViewModel
        {
            UserId = user.Name,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Deactivated = user.Deactivated,
            Admin = user.Admin == true,
            CreationTs = user.CreationTs,
            UserType = user.UserType ?? "user",
            Locked = user.Locked,
            IsGuest = user.IsGuest == true
        };
    }

    public static List<UserListViewModel> ToViewModels(this IEnumerable<SynapseAdminUserListResult.SynapseAdminUserListResultUser> users)
    {
        return users.Select(ToViewModel).ToList();
    }

    public static UserDetailViewModel ToDetailViewModel(this SynapseAdminUserListResult.SynapseAdminUserListResultUser user)
    {
        return new UserDetailViewModel
        {
            UserId = user.Name,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Deactivated = user.Deactivated,
            Admin = user.Admin == true,
            CreationTs = DateTimeOffset.FromUnixTimeSeconds(user.CreationTs).ToUnixTimeMilliseconds(),
            UserType = user.UserType ?? "user",
            Locked = user.Locked,
            ShadowBanned = user.ShadowBanned,
            ConsentVersion = user.ConsentVersion ?? "",
            ConsentServerNoticeSent = user.ConsentServerNoticeSent ?? "",
            AppserviceId = user.AppserviceId ?? ""
        };
    }
}
