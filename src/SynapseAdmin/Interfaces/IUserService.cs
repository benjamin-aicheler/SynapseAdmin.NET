using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IUserService
{
    Task<OperationResult<(int Total, List<UserListViewModel> Users)>> GetUserListAsync(int offset, int limit, string orderBy, SortDirection direction, CancellationToken token = default);
    Task<OperationResult<UserDetailViewModel>> GetUserDetailsAsync(string userId);
    Task<OperationResult> DeactivateUserAsync(string userId, bool erase = false);
    Task<OperationResult> QuarantineMediaAsync(string userId);
    Task<OperationResult<string>> LoginAsUserAsync(string userId, TimeSpan expireIn);
    Task<OperationResult> SendServerNoticeAsync(string userId, string message);
    Task<OperationResult<List<UserMediaStatisticsViewModel>>> GetTopMediaUsersAsync(int count = 10);
    Task<OperationResult> CreateUserAsync(UserCreateViewModel model);
}
