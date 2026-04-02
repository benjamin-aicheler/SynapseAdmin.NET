using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IRoomService
{
    Task<OperationResult<(int Total, List<RoomListViewModel> Rooms)>> GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default);
    Task<OperationResult<RoomDetailViewModel>> GetRoomDetailsAsync(string roomId);
    Task<OperationResult> DeleteRoomAsync(string roomId, bool block = false, bool purge = true);
    Task<OperationResult> QuarantineMediaAsync(string roomId);
    Task<OperationResult> BlockRoomAsync(string roomId, bool block);
}
