using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class Users
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IUserService UserService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        private MudTable<UserListViewModel>? table;
        private int? totalUsers;

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task<TableData<UserListViewModel>> ServerReload(TableState state, CancellationToken token)
        {
            var offset = state.Page * state.PageSize;
            var orderBy = state.SortLabel ?? "name";

            var result = await UserService.GetUserListAsync(offset, state.PageSize, orderBy, state.SortDirection, token: token);
            
            if (result.Success && result.Data != default)
            {
                totalUsers = result.Data.Total;
                StateHasChanged();
                return new TableData<UserListViewModel>() { TotalItems = result.Data.Total, Items = result.Data.Users };
            }
            
            if (!result.Success)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
            
            return new TableData<UserListViewModel>() { TotalItems = 0, Items = new List<UserListViewModel>() };
        }
    }
}
