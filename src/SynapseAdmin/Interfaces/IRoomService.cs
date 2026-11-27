using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Interfaces;

public interface IRoomService
{
    Task<OperationResult<(int Total, List<RoomListViewModel> Rooms)>> GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default);
    Task<OperationResult<RoomDetailViewModel>> GetRoomDetailsAsync(string roomId, CancellationToken token = default);
    /// <summary>
    /// Requests the deletion of a room, which the homeserver runs in the background. The data is the deletion's
    /// <c>delete_id</c>, or empty if the homeserver returned none (#518).
    /// </summary>
    Task<OperationResult<string>> DeleteRoomAsync(string roomId, bool block = false, bool purge = true, CancellationToken token = default);
    /// <summary>The status of a room deletion; a deletion the homeserver no longer knows is <see cref="RoomDeletionState.Unknown"/>.</summary>
    Task<OperationResult<RoomDeletionStatusViewModel>> GetRoomDeletionStatusAsync(string deleteId, CancellationToken token = default);
    /// <summary>The room's running deletion (active before scheduled), or <c>null</c> if there is none.</summary>
    Task<OperationResult<RoomDeletionStatusViewModel?>> GetActiveRoomDeletionAsync(string roomId, CancellationToken token = default);
    Task<OperationResult> QuarantineMediaAsync(string roomId, CancellationToken token = default);
    Task<OperationResult> BlockRoomAsync(string roomId, bool block, CancellationToken token = default);
    Task<OperationResult<RoomBlockStatusViewModel>> GetRoomBlockStatusAsync(string roomId, CancellationToken token = default);
    /// <summary>
    /// Grants a user the highest power level of a local user who is joined to the room, and has the homeserver invite
    /// the user if they aren't in the room and it isn't public (#587). A blank <paramref name="userId"/> means the
    /// signed-in admin; any other value must be a full user ID, on any server, or the call is refused with a warning and
    /// nothing is sent.
    /// </summary>
    Task<OperationResult> MakeRoomAdminAsync(string roomId, string? userId, CancellationToken token = default);
    /// <summary>
    /// Joins a local user to a room; the homeserver first invites the user as the signed-in admin unless the room is
    /// public (#587). <paramref name="roomIdOrAlias"/> must have the form of a room ID or a room alias, as the user page
    /// lets the admin type it (#605, #606). <paramref name="serverNames"/> are the servers to join through, for a room
    /// this server isn't in: they are trimmed, blanks and duplicates dropped, and each must be a server name; they are
    /// refused with an alias, whose resolution returns its own. <paramref name="userId"/> must be a full user ID on the
    /// signed-in admin's server, compared exactly. The checks run in that order (room, servers, user); any refusal is a
    /// warning and nothing is sent. The result names the room ID the homeserver joined.
    /// </summary>
    Task<OperationResult> JoinUserToRoomAsync(string roomIdOrAlias, string userId, IReadOnlyList<string>? serverNames = null,
        CancellationToken token = default);
    Task<OperationResult<List<RoomStatisticsViewModel>>> GetLargestRoomsAsync(CancellationToken token = default);
    Task<OperationResult<RoomMessagesViewModel>> GetRoomMessagesAsync(string roomId, string? from = null, int limit = 10, string dir = "f", string? filter = null, string? to = null, CancellationToken token = default);
    Task<OperationResult<List<RoomMediaItemViewModel>>> GetMediaMetadataBatchAsync(List<string> mxcUris, CancellationToken token = default);
    Task<OperationResult<string>> PurgeRoomHistoryAsync(string roomId, PurgeHistoryViewModel request, CancellationToken token = default);
    Task<OperationResult<SynapseAdminPurgeHistoryStatusResponse>> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken token = default);
    Task<OperationResult<PublicRoomDirectoryViewModel>> GetPublicRoomsAsync(int limit, string? since = null, string? searchTerm = null, CancellationToken token = default);
    Task<OperationResult<string>> GetRoomDirectoryVisibilityAsync(string roomId, CancellationToken token = default);
    Task<OperationResult> SetRoomDirectoryVisibilityAsync(string roomId, string visibility, CancellationToken token = default);
    Task<OperationResult> PutRoomAliasAsync(string roomAlias, string roomId, CancellationToken token = default);
    Task<OperationResult> DeleteRoomAliasAsync(string roomAlias, CancellationToken token = default);
    Task<OperationResult<List<string>>> GetLocalRoomAliasesAsync(string roomId, CancellationToken token = default);
    string? GetActivePurgeId(string roomId);
    void SetActivePurgeId(string roomId, string purgeId);
    void ClearActivePurgeId(string roomId);
    /// <summary>
    /// Clears the tracked purge of a room only if it is still <paramref name="expectedPurgeId"/>, so finishing or
    /// giving up on an older purge never removes a newer one (the cache is shared by every session, #522).
    /// </summary>
    void ClearActivePurgeId(string roomId, string expectedPurgeId);
}
