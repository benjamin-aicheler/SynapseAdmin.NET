using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IUserService
{
    Task<OperationResult<(int Total, List<UserListViewModel> Users)>> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, CancellationToken token = default);
    Task<OperationResult<UserDetailViewModel>> GetUserDetailsAsync(string userId, CancellationToken token = default);
    Task<OperationResult> DeactivateUserAsync(string userId, bool erase = false, CancellationToken token = default);
    Task<OperationResult> QuarantineMediaAsync(string userId, CancellationToken token = default);
    Task<OperationResult<string>> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken token = default);
    Task<OperationResult> SendServerNoticeAsync(string userId, string message, CancellationToken token = default);
    Task<OperationResult<List<UserMediaStatisticsViewModel>>> GetTopMediaUsersAsync(int count = 10, CancellationToken token = default);
    Task<OperationResult> CreateUserAsync(UserCreateViewModel model, CancellationToken token = default);
    Task<OperationResult<List<UserMembershipViewModel>>> GetUserMembershipsAsync(string userId, CancellationToken token = default);
}
