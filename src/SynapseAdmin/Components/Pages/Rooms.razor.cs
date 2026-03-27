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
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                try
                {
                    var offset = state.Page * state.PageSize;
                    // Simple ordering support
                    var orderBy = state.SortLabel ?? "name";
                    var dir = state.SortDirection == SortDirection.Descending ? "b" : "f";

                    var url = $"/_synapse/admin/v1/rooms?from={offset}&limit={state.PageSize}&dir={dir}&order_by={orderBy}";

                    var result = await synapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminRoomListResult>(url, cancellationToken: token);
                    
                    if (result != null)
                    {
                        totalRooms = result.TotalRooms;
                        return new TableData<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>() { TotalItems = result.TotalRooms, Items = result.Rooms };
                    }
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error fetching rooms: {ex.Message}", Severity.Error);
                }
            }

            return new TableData<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>() { TotalItems = 0, Items = new List<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>() };
        }
    }
}