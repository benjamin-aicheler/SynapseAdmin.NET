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
        public NavigationManager Navigation { get; set; } = null!;

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
            if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
            {
                try
                {
                    var offset = state.Page * state.PageSize;
                    var orderBy = state.SortLabel ?? "name";
                    var dir = state.SortDirection == SortDirection.Descending ? "b" : "f";

                    // Depending on the Synapse version, /_synapse/admin/v2/users supports offset/from.
                    var url = $"/_synapse/admin/v2/users?from={offset}&limit={state.PageSize}"; // we can also use search params

                    var result = await synapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminUserListResult>(url, cancellationToken: token);
                    
                    if (result != null)
                    {
                        totalUsers = result.Total;
                        return new TableData<SynapseAdminUserListResult.SynapseAdminUserListResultUser>() { TotalItems = result.Total, Items = result.Users };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching users: {ex.Message}");
                }
            }

            return new TableData<SynapseAdminUserListResult.SynapseAdminUserListResultUser>() { TotalItems = 0, Items = new List<SynapseAdminUserListResult.SynapseAdminUserListResultUser>() };
        }
    }
}