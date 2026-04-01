using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages
{
    public partial class FederationDestinations
    {
        [Inject]
        public MatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public FederationService FederationService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        private MudTable<FederationDestinationListViewModel>? table;
        private int? totalDestinations;

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task<TableData<FederationDestinationListViewModel>> ServerReload(TableState state, CancellationToken token)
        {
            try
            {
                var offset = state.Page * state.PageSize;

                var (total, destinations) = await FederationService.GetDestinationsAsync(offset, state.PageSize, state.SortDirection, token: token);

                totalDestinations = total;
                StateHasChanged();
                return new TableData<FederationDestinationListViewModel>() { TotalItems = total, Items = destinations };
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error fetching destinations: {ex.Message}", Severity.Error);
            }

            return new TableData<FederationDestinationListViewModel>() { TotalItems = 0, Items = new List<FederationDestinationListViewModel>() };
        }

        private async Task ResetConnection(string destination)
        {
            bool? result = await DialogService.ShowMessageBoxAsync(
                "Reset Connection", 
                $"Are you sure you want to reset the federation connection backoff for {destination}?", 
                yesText: "Reset", cancelText: "Cancel");
                
            if (result == true)
            {
                try {
                    await FederationService.ResetConnectionTimeoutAsync(destination);
                    Snackbar.Add($"Connection backoff reset for {destination}.", Severity.Success);
                    await ReloadTable();
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error resetting connection for {destination}: {ex.Message}", Severity.Error);
                }
            }
        }
    }
}
