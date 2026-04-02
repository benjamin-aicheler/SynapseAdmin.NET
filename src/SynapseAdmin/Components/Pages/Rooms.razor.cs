using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class Rooms
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IRoomService RoomService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        private MudTable<RoomListViewModel>? table;
        private int? totalRooms;

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task<TableData<RoomListViewModel>> ServerReload(TableState state, CancellationToken token)
        {
            var offset = state.Page * state.PageSize;
            var orderBy = state.SortLabel ?? "name";

            var result = await RoomService.GetRoomListAsync(offset, state.PageSize, orderBy, state.SortDirection, token: token);
            
            if (result.Success && result.Data != default)
            {
                totalRooms = result.Data.Total;
                StateHasChanged();
                return new TableData<RoomListViewModel>() { TotalItems = result.Data.Total, Items = result.Data.Rooms };
            }
            
            if (!result.Success)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
            
            return new TableData<RoomListViewModel>() { TotalItems = 0, Items = new List<RoomListViewModel>() };
        }
    }
}
