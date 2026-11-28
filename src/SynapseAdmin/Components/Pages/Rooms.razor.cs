using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class Rooms
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IRoomService RoomService { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        private MudTable<RoomListViewModel>? table;
        private int? totalRooms;
        private string? _searchTerm;
        private RoomListFilterChoice _publicRooms = RoomListFilterChoice.All;
        private RoomListFilterChoice _emptyRooms = RoomListFilterChoice.All;

        /// <summary>Whether the count above the table counts matching rooms rather than every room on the server.</summary>
        private bool IsFiltered =>
            !string.IsNullOrEmpty(_searchTerm) || _publicRooms != RoomListFilterChoice.All || _emptyRooms != RoomListFilterChoice.All;

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        /// <summary>
        /// A new search term or filter can leave fewer pages than the one shown, so the list starts again at the
        /// first page (#588).
        /// </summary>
        private async Task ReloadFromFirstPage()
        {
            if (table == null) return;
            if (table.CurrentPage != 0)
            {
                table.NavigateTo(0);
                return;
            }
            await table.ReloadServerData();
        }

        private async Task OnSearchChanged(string text)
        {
            _searchTerm = text;
            await ReloadFromFirstPage();
        }

        private async Task OnPublicRoomsChanged(RoomListFilterChoice choice)
        {
            _publicRooms = choice;
            await ReloadFromFirstPage();
        }

        private async Task OnEmptyRoomsChanged(RoomListFilterChoice choice)
        {
            _emptyRooms = choice;
            await ReloadFromFirstPage();
        }

        private async Task<TableData<RoomListViewModel>> ServerReload(TableState state, CancellationToken token)
        {
            var offset = state.Page * state.PageSize;
            var orderBy = state.SortLabel ?? "name";

            var result = await RoomService.GetRoomListAsync(offset, state.PageSize, orderBy, state.SortDirection, _searchTerm,
                _publicRooms.ToFlag(), _emptyRooms.ToFlag(), token);

            if (result.Success && result.Data != default)
            {
                totalRooms = result.Data.Total;
                StateHasChanged();
                return new TableData<RoomListViewModel>() { TotalItems = result.Data.Total, Items = result.Data.Rooms };
            }

            Snackbar.ShowFailure(result);

            return new TableData<RoomListViewModel>() { TotalItems = 0, Items = new List<RoomListViewModel>() };
        }

    }
}
