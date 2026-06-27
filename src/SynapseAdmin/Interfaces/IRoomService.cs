using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Interfaces;

public interface IRoomService
{
    Task<OperationResult<(int Total, List<RoomListViewModel> Rooms)>> GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default);
    Task<OperationResult<RoomDetailViewModel>> GetRoomDetailsAsync(string roomId, CancellationToken token = default);
    Task<OperationResult> DeleteRoomAsync(string roomId, bool block = false, bool purge = true, CancellationToken token = default);
    Task<OperationResult> QuarantineMediaAsync(string roomId, CancellationToken token = default);
    Task<OperationResult> BlockRoomAsync(string roomId, bool block, CancellationToken token = default);
    Task<OperationResult<List<RoomStatisticsViewModel>>> GetLargestRoomsAsync(CancellationToken token = default);
    Task<OperationResult<RoomMessagesViewModel>> GetRoomMessagesAsync(string roomId, string? from = null, int limit = 10, string dir = "f", string? filter = null, string? to = null, CancellationToken token = default);
    Task<OperationResult<List<RoomMediaItemViewModel>>> GetMediaMetadataBatchAsync(List<string> mxcUris, CancellationToken token = default);
    Task<OperationResult<SynapseAdminPurgeHistoryResponse>> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken token = default);
    Task<OperationResult<SynapseAdminPurgeHistoryStatusResponse>> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken token = default);
    Task<OperationResult<MatrixPublicRoomDirectoryResult>> GetPublicRoomsAsync(int limit, string? since = null, string? searchTerm = null, CancellationToken token = default);
    Task<OperationResult<string>> GetRoomDirectoryVisibilityAsync(string roomId, CancellationToken token = default);
    Task<OperationResult> SetRoomDirectoryVisibilityAsync(string roomId, string visibility, CancellationToken token = default);
    Task<OperationResult> PutRoomAliasAsync(string roomAlias, string roomId, CancellationToken token = default);
    Task<OperationResult> DeleteRoomAliasAsync(string roomAlias, CancellationToken token = default);
    Task<OperationResult<List<string>>> GetLocalRoomAliasesAsync(string roomId, CancellationToken token = default);
    string? GetActivePurgeId(string roomId);
    void SetActivePurgeId(string roomId, string purgeId);
    void ClearActivePurgeId(string roomId);
}
