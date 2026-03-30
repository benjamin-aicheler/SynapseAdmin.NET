using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using MudBlazor;

namespace SynapseAdmin.Services;

public class EventReportService(MatrixSessionService sessionService)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<(int Total, List<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport> Reports)> GetEventReportsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return (0, []);

        var dir = direction == SortDirection.Ascending ? "f" : "b";
        var url = $"/_synapse/admin/v1/event_reports?from={offset}&limit={limit}&dir={dir}";

        var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminEventReportListResult>(url, cancellationToken: token);
        if (result == null) return (0, []);
        
        return (result.Total, result.Reports);
    }

    public async Task DeleteEventReportAsync(string reportId)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.DeleteEventReportAsync(reportId);
    }
}
