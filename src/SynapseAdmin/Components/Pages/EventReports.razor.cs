using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class EventReports
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;

        [Inject]
        public IEventReportService EventReportService { get; set; } = null!;

        [Inject]
        public NavigationManager Navigation { get; set; } = null!;

        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        private MudTable<EventReportListViewModel>? table;
        private int? totalReports;

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task<TableData<EventReportListViewModel>> ServerReload(TableState state, CancellationToken token)
        {
            var offset = state.Page * state.PageSize;

            var result = await EventReportService.GetEventReportsAsync(offset, state.PageSize, state.SortDirection, token: token);
            
            if (result.Success && result.Data != default)
            {
                totalReports = result.Data.Total;
                StateHasChanged();
                return new TableData<EventReportListViewModel>() { TotalItems = result.Data.Total, Items = result.Data.Reports };
            }
            
            if (!result.Success)
            {
                Snackbar.Add(result.Message, result.Severity);
            }

            return new TableData<EventReportListViewModel>() { TotalItems = 0, Items = new List<EventReportListViewModel>() };
        }

        private async Task DeleteReport(string reportId)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DismissReportTitle"], 
                L["DismissReportConfirmation"], 
                yesText: L["Dismiss"], cancelText: L["Cancel"]);
                
            if (confirmed == true)
            {
                var result = await EventReportService.DeleteEventReportAsync(reportId);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    await ReloadTable();
                }
            }
        }
    }
}
