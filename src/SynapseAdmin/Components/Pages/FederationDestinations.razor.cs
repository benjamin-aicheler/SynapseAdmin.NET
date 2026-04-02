using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class FederationDestinations
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IFederationService FederationService { get; set; } = null!;
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
            var offset = state.Page * state.PageSize;

            var result = await FederationService.GetDestinationsAsync(offset, state.PageSize, state.SortDirection, token: token);

            if (result.Success && result.Data != default)
            {
                totalDestinations = result.Data.Total;
                StateHasChanged();
                return new TableData<FederationDestinationListViewModel>() { TotalItems = result.Data.Total, Items = result.Data.Destinations };
            }
            
            if (!result.Success)
            {
                Snackbar.Add(result.Message, result.Severity);
            }

            return new TableData<FederationDestinationListViewModel>() { TotalItems = 0, Items = new List<FederationDestinationListViewModel>() };
        }

        private async Task ResetConnection(string destination)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                "Reset Connection", 
                $"Are you sure you want to reset the federation connection backoff for {destination}?", 
                yesText: "Reset", cancelText: "Cancel");
                
            if (confirmed == true)
            {
                var result = await FederationService.ResetConnectionTimeoutAsync(destination);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    await ReloadTable();
                }
            }
        }
    }
}
