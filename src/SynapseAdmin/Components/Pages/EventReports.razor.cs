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
         public EventReportService EventReportService { get; set; } = null!;

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
        try
        {
            var offset = state.Page * state.PageSize;

            var (total, reports) = await EventReportService.GetEventReportsAsync(offset, state.PageSize, state.SortDirection, token: token);
            
            totalReports = total;
            StateHasChanged();
            return new TableData<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport>() { TotalItems = total, Items = reports };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching event reports: {ex.Message}");
            Snackbar.Add($"Error fetching event reports: {ex.Message}", Severity.Error);
        }

        return new TableData<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport>() { TotalItems = 0, Items = new List<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport>() };
    }

    private async Task DeleteReport(string reportId)
    {
        bool? result = await DialogService.ShowMessageBoxAsync(
            "Dismiss Report", 
            "Are you sure you want to dismiss (delete) this report? The reported event will not be deleted from the room.", 
            yesText: "Dismiss", cancelText: "Cancel");
            
        if (result == true)
        {
            try {
                await EventReportService.DeleteEventReportAsync(reportId);
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
