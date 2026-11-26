using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IUserService
{
    Task<OperationResult<(int Total, List<UserListViewModel> Users)>> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default);
    Task<OperationResult<UserDetailViewModel>> GetUserDetailsAsync(string userId, CancellationToken token = default);

    /// <summary>One page of the user's uploads, newest first. <paramref name="offset"/> is the item offset.</summary>
    Task<OperationResult<UserMediaPageViewModel>> GetUserMediaPageAsync(string userId, int offset, int limit, CancellationToken token = default);

    /// <summary>Upload count and total bytes across all of the user's media.</summary>
    Task<OperationResult<UserMediaTotalsViewModel>> GetUserMediaTotalsAsync(string userId, CancellationToken token = default);
    Task<OperationResult> DeactivateUserAsync(string userId, bool erase = false, CancellationToken token = default);
    Task<OperationResult> QuarantineMediaAsync(string userId, CancellationToken token = default);
    Task<OperationResult<string>> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken token = default);
    Task<OperationResult> SendServerNoticeAsync(string userId, string message, CancellationToken token = default);
    Task<OperationResult<List<UserMediaStatisticsViewModel>>> GetTopMediaUsersAsync(int count = 10, CancellationToken token = default);
    Task<OperationResult> CreateUserAsync(UserCreateViewModel model, CancellationToken token = default);

    /// <summary>Locks or unlocks an existing account. Fails with "user not found" for an account the homeserver doesn't know.</summary>
    Task<OperationResult> SetUserLockedAsync(string userId, bool locked, CancellationToken token = default);

    /// <summary>Grants or revokes server admin rights of an existing account. Fails with "user not found" for an unknown account.</summary>
    Task<OperationResult> SetUserAdminAsync(string userId, bool admin, CancellationToken token = default);

    Task<OperationResult> SetUserSuspendedAsync(string userId, bool suspended, CancellationToken token = default);
    Task<OperationResult> SetUserShadowBannedAsync(string userId, bool shadowBanned, CancellationToken token = default);

    /// <summary>Sets a new password, logging out all of the user's devices only if <paramref name="logoutDevices"/> is set.</summary>
    Task<OperationResult> ResetPasswordAsync(string userId, string newPassword, bool logoutDevices, CancellationToken token = default);

    /// <summary>
    /// Reactivates a deactivated account, with a new password if <paramref name="newPassword"/> isn't empty. Fails with
    /// "user not found" for an unknown account and with a warning for an active one.
    /// </summary>
    Task<OperationResult> ReactivateUserAsync(string userId, string? newPassword, CancellationToken token = default);
    Task<OperationResult<List<UserMembershipViewModel>>> GetUserMembershipsAsync(string userId, CancellationToken token = default);
    Task<OperationResult<List<UserExperimentalFeatureViewModel>>> GetUserExperimentalFeaturesAsync(string userId, CancellationToken token = default);
    Task<OperationResult> SetUserExperimentalFeaturesAsync(string userId, Dictionary<string, bool> features, CancellationToken token = default);
    Task<OperationResult<long>> RenewAccountValidityAsync(AccountValidityRenewViewModel model, CancellationToken token = default);
}
