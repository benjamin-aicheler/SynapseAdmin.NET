using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using LibMatrix.EventTypes.Spec.State.RoomInfo;
using SynapseAdmin.Models.ViewModels;
using MudBlazor;

namespace SynapseAdmin.Services;

public class RoomService(MatrixSessionService sessionService)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<(int Total, List<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom> Rooms)> GetRoomListAsync(int offset, int limit, string orderBy, SortDirection direction, string? searchTerm = null, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return (0, []);

        var dir = direction == SortDirection.Descending ? "b" : "f";
        var url = $"/_synapse/admin/v1/rooms?from={offset}&limit={limit}&dir={dir}&order_by={orderBy}";
        if (!string.IsNullOrEmpty(searchTerm))
        {
            url += $"&search_term={Uri.EscapeDataString(searchTerm)}";
        }

        var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult>(url, cancellationToken: token);
        if (result == null) return (0, []);
        
        return (result.TotalRooms, result.Rooms);
    }

    public async Task<RoomDetailViewModel?> GetRoomDetailsAsync(string roomId)
    {
        if (SynapseAdmin == null) return null;

        var encodedRoomId = Uri.EscapeDataString(roomId);
        var roomDetails = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>($"/_synapse/admin/v1/rooms/{encodedRoomId}");
        
        if (roomDetails == null) return null;

        var membersTask = SynapseAdmin.Admin.GetRoomMembersAsync(roomId);
        var stateTask = SynapseAdmin.Admin.GetRoomStateAsync(roomId);
        
        await Task.WhenAll(membersTask, stateTask);

        var stateEvents = stateTask.Result;
        var tombstone = stateEvents?.Events
            .FirstOrDefault(x => x.Type == RoomTombstoneEventContent.EventId)?
            .ContentAs<RoomTombstoneEventContent>();

        return new RoomDetailViewModel
        {
            Details = roomDetails,
            Members = membersTask.Result,
            StateEvents = stateEvents,
            Tombstone = tombstone
        };
    }

    public async Task DeleteRoomAsync(string roomId, bool block = false, bool purge = true)
    {
        if (SynapseAdmin == null) return;

        var req = new SynapseAdminRoomDeleteRequest
        {
            Block = block,
            Purge = purge
        };
        await SynapseAdmin.Admin.DeleteRoom(roomId, req);
    }

    public async Task QuarantineMediaAsync(string roomId)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.QuarantineMediaByRoomId(roomId);
    }

    public async Task BlockRoomAsync(string roomId, bool block)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.BlockRoom(roomId, block);
    }
}
