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
            CreationTs = user.CreationTs is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToUnixTimeMilliseconds() : null,
            UserType = user.UserType ?? "user",
            Locked = user.Locked,
            ShadowBanned = user.ShadowBanned,
            Suspended = user.Suspended == true,
            ConsentVersion = user.ConsentVersion ?? "",
            ConsentServerNoticeSent = user.ConsentServerNoticeSent ?? "",
            AppserviceId = user.AppserviceId ?? "",
            ExpirationTs = user.ExpirationTs
        };
    }

    public static UserMediaItemViewModel ToViewModel(this SynapseAdminUserMediaResult.MediaInfo media)
    {
        return new UserMediaItemViewModel
        {
            MediaId = media.MediaId,
            UploadName = media.UploadName,
            MediaType = media.MediaType,
            MediaLength = media.MediaLength,
            CreatedTimestamp = media.CreatedTimestamp,
            QuarantinedBy = media.QuarantinedBy,
            SafeFromQuarantine = media.SafeFromQuarantine
        };
    }

    public static UserMediaStatisticsViewModel ToViewModel(this UserMediaStatisticsResponse.UserMediaStatistic user)
    {
        return new UserMediaStatisticsViewModel
        {
            UserId = user.UserId,
            DisplayName = user.DisplayName,
            MediaCount = user.MediaCount,
            TotalSize = user.MediaLength
        };
    }

    public static List<UserMembershipViewModel> ToViewModels(this SynapseAdminUserMembershipsResponse response)
    {
        return response.Memberships.Select(kvp => new UserMembershipViewModel
        {
            RoomId = kvp.Key,
            Membership = kvp.Value
        }).ToList();
    }

    /// <summary>
    /// Synapse sends the status in lower case; any other value is <see cref="UserRedactionState.Unknown"/> (#600). The
    /// status has no ID of its own, so the caller passes the <paramref name="redactId"/> it asked for. Only the number
    /// of failed redactions is copied: their event IDs and error texts, and the error of a failed redaction, go only
    /// to the log.
    /// </summary>
    public static UserRedactionStatusViewModel ToViewModel(this SynapseAdminUserRedactStatusResponse status, string redactId) => new()
    {
        RedactId = redactId,
        State = status.Status switch
        {
            "scheduled" => UserRedactionState.Scheduled,
            "active" => UserRedactionState.Active,
            "complete" => UserRedactionState.Complete,
            "failed" => UserRedactionState.Failed,
            _ => UserRedactionState.Unknown
        },
        FailedRedactionCount = status.FailedRedactions?.Count ?? 0
    };
}
