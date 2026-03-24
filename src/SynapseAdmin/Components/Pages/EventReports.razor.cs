using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Services;

namespace SynapseAdmin.Components.Pages
{
    public partial class EventReports
    {
        [Inject]
         public MatrixSessionService MatrixSession { get; set; } = null!;

         [Inject]
         public NavigationManager Navigation { get; set; } = null!;

         [Inject]
         public ISnackbar Snackbar { get; set; } = null!;

         [Inject]
         public IDialogService DialogService { get; set; } = null!;

         private MudTable<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport>? table;
    private int? totalReports;

    private async Task ReloadTable()
    {
        if (table != null)
        {
            await table.ReloadServerData();
        }
    }

    private async Task<TableData<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport>> ServerReload(TableState state, CancellationToken token)
    {
        if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
        {
            try
            {
                var offset = state.Page * state.PageSize;
                
                // Using dir for sorting because API allows it
                var dir = state.SortDirection == SortDirection.Ascending ? "f" : "b";

                var url = $"/_synapse/admin/v1/event_reports?from={offset}&limit={state.PageSize}&dir={dir}";

                var result = await synapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminEventReportListResult>(url, cancellationToken: token);
                
                if (result != null)
                {
                    totalReports = result.Total;
                    return new TableData<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport>() { TotalItems = result.Total, Items = result.Reports };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching event reports: {ex.Message}");
                Snackbar.Add($"Error fetching event reports: {ex.Message}", Severity.Error);
            }
        }

        return new TableData<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport>() { TotalItems = 0, Items = new List<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport>() };
    }

    private async Task DeleteReport(string reportId)
    {
        if (MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse synapseAdmin)
        {
            bool? result = await DialogService.ShowMessageBoxAsync(
                "Dismiss Report", 
                "Are you sure you want to dismiss (delete) this report? The reported event will not be deleted from the room.", 
                yesText: "Dismiss", cancelText: "Cancel");
                
            if (result == true)
            {
                try {
                    // LibMatrix provides DeleteEventReportAsync
                    await synapseAdmin.Admin.DeleteEventReportAsync(reportId);
                    Snackbar.Add("Report dismissed successfully.", Severity.Success);
                    await ReloadTable();
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error dismissing report: {ex.Message}", Severity.Error);
                }
            }
        }
    }
    }
}