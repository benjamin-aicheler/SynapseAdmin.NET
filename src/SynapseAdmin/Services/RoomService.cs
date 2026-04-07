using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using LibMatrix.EventTypes.Spec.State.RoomInfo;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;

namespace SynapseAdmin.Services;

public class RoomService(IMatrixSessionService sessionService, ILogger<RoomService> logger, IStringLocalizer<SharedResources> L) : IRoomService
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<OperationResult<(int Total, List<RoomListViewModel> Rooms)>> GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Failure(L["NotAuthenticated"]);

        try
        {
            if (orderBy == "room_id") orderBy = "alphabetical";
            var dir = direction == SortDirection.Descending ? "b" : "f";
            var url = $"/_synapse/admin/v1/rooms?from={offset}&limit={limit}&dir={dir}&order_by={Uri.EscapeDataString(orderBy)}";
            if (!string.IsNullOrEmpty(searchTerm))
            {
                url += $"&search_term={Uri.EscapeDataString(searchTerm)}";
            }

            var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult>(url, cancellationToken: token);
            if (result == null) return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Ok((0, []));
            
            var vms = result.Rooms.Select(r => new RoomListViewModel
            {
                RoomId = r.RoomId,
                Name = r.Name,
                CanonicalAlias = r.CanonicalAlias,
                JoinedMembers = r.JoinedMembers,
                JoinedLocalMembers = r.JoinedLocalMembers,
                Version = r.Version ?? "1",
                Creator = r.Creator ?? "",
                Encryption = r.Encryption,
                Federated = r.Federatable,
                Public = r.Public,
                AvatarUrl = "", 
                JoinRules = r.JoinRules,
                RoomType = "" 
            }).ToList();

            return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Ok((result.TotalRooms, vms));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching room list (offset: {Offset}, limit: {Limit})", offset, limit);
            return OperationResult<(int Total, List<RoomListViewModel> Rooms)>.Failure(L["ErrorFetchingRoomList"]);
        }
    }

    public async Task<OperationResult<RoomDetailViewModel>> GetRoomDetailsAsync(string roomId)
    {
        if (SynapseAdmin == null) return OperationResult<RoomDetailViewModel>.Failure(L["NotAuthenticated"]);

        try
        {
            var encodedRoomId = Uri.EscapeDataString(roomId);
            var r = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>($"/_synapse/admin/v1/rooms/{encodedRoomId}");
            
            if (r == null) return OperationResult<RoomDetailViewModel>.Failure(L["RoomNotFound"]);

            var membersTask = SynapseAdmin.Admin.GetRoomMembersAsync(roomId);
            var stateTask = SynapseAdmin.Admin.GetRoomStateAsync(roomId);

            await Task.WhenAll(membersTask, stateTask);

            var members = await membersTask;
            var stateEvents = await stateTask;
            
            var tombstone = stateEvents?.Events
                .FirstOrDefault(x => x.Type == RoomTombstoneEventContent.EventId)?
                .ContentAs<RoomTombstoneEventContent>();

            var vm = new RoomDetailViewModel
            {
                RoomId = r.RoomId,
                Name = r.Name,
                CanonicalAlias = r.CanonicalAlias,
                JoinedMembers = r.JoinedMembers,
                JoinedLocalMembers = r.JoinedLocalMembers,
                Version = r.Version ?? "1",
                Creator = r.Creator ?? "",
                Encryption = r.Encryption,
                Federated = r.Federatable,
                Public = r.Public,
                AvatarUrl = "",
                JoinRules = r.JoinRules,
                GuestAccess = r.GuestAccess,
                HistoryVisibility = r.HistoryVisibility,
                RoomType = "",
                Forgotten = false, 
                IsTombstoned = tombstone != null,
                ReplacementRoom = tombstone?.ReplacementRoom,
                Members = members?.Members ?? [],
                StateEvents = stateEvents?.Events.Select(e => new RoomStateEventViewModel
                {
                    Type = e.Type,
                    StateKey = e.StateKey,
                    Sender = e.Sender,
                    RawContent = e.RawContent?.ToJsonString()
                }).ToList() ?? []
            };
            return OperationResult<RoomDetailViewModel>.Ok(vm);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching room details for {RoomId}", roomId.SanitizeForLogging());
            return OperationResult<RoomDetailViewModel>.Failure(L["ErrorFetchingRoomDetails"]);
        }
    }

    public async Task<OperationResult> DeleteRoomAsync(string roomId, bool block = false, bool purge = true)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            var req = new SynapseAdminRoomDeleteRequest
            {
                Block = block,
                Purge = purge
            };
            await SynapseAdmin.Admin.DeleteRoom(roomId, req);
            logger.LogInformation("Successfully deleted room {RoomId} (block: {Block}, purge: {Purge})", roomId.SanitizeForLogging(), block, purge);
            return OperationResult.Ok(L["RoomDeletedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorDeletingRoom"]);
        }
    }

    public async Task<OperationResult> QuarantineMediaAsync(string roomId)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await SynapseAdmin.Admin.QuarantineMediaByRoomId(roomId);
            logger.LogInformation("Successfully quarantined media for room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult.Ok(L["RoomMediaQuarantinedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error quarantining media for room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorQuarantiningRoomMedia"]);
        }
    }

    public async Task<OperationResult> BlockRoomAsync(string roomId, bool block)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await SynapseAdmin.Admin.BlockRoom(roomId, block);
            logger.LogInformation("Successfully {Action} room {RoomId}", block ? "blocked" : "unblocked", roomId.SanitizeForLogging());
            return OperationResult.Ok(block ? L["RoomBlockedSuccessfully"] : L["RoomUnblockedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error blocking/unblocking room {RoomId}", roomId.SanitizeForLogging());
            return OperationResult.Failure(block ? L["ErrorBlockingRoom"] : L["ErrorUnblockingRoom"]);
        }
    }
}
