using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages
{
    public partial class Rooms
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public RoomService RoomService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        private MudTable<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>? table;
        private int? totalRooms;

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task<TableData<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>> ServerReload(TableState state, CancellationToken token)
        {
            try
            {
                var offset = state.Page * state.PageSize;
                var orderBy = state.SortLabel ?? "name";

                var (total, rooms) = await RoomService.GetRoomListAsync(offset, state.PageSize, orderBy, state.SortDirection, token: token);
                
                totalRooms = total;
                StateHasChanged();
                return new TableData<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>() { TotalItems = total, Items = rooms };
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error fetching rooms: {ex.Message}", Severity.Error);
            }

            return new TableData<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>() { TotalItems = 0, Items = new List<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>() };
        }
    }
}
