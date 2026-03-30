using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages
{
    public partial class Users
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public UserService UserService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        private MudTable<SynapseAdminUserListResult.SynapseAdminUserListResultUser>? table;
        private int? totalUsers;

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task<TableData<SynapseAdminUserListResult.SynapseAdminUserListResultUser>> ServerReload(TableState state, CancellationToken token)
        {
            try
            {
                var offset = state.Page * state.PageSize;
                var orderBy = state.SortLabel ?? "name";

                var (total, users) = await UserService.GetUserListAsync(offset, state.PageSize, orderBy, state.SortDirection, token: token);
                
                totalUsers = total;
                StateHasChanged();
                return new TableData<SynapseAdminUserListResult.SynapseAdminUserListResultUser>() { TotalItems = total, Items = users };
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error fetching users: {ex.Message}", Severity.Error);
            }

            return new TableData<SynapseAdminUserListResult.SynapseAdminUserListResultUser>() { TotalItems = 0, Items = new List<SynapseAdminUserListResult.SynapseAdminUserListResultUser>() };
        }
    }
}
