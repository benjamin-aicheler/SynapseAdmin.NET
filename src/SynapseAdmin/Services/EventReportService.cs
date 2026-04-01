using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Services;

public class EventReportService(MatrixSessionService sessionService)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<(int Total, List<EventReportListViewModel> Reports)> GetEventReportsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return (0, []);

        var dir = direction == SortDirection.Ascending ? "f" : "b";
        var url = $"/_synapse/admin/v1/event_reports?from={offset}&limit={limit}&dir={dir}";

        var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminEventReportListResult>(url, cancellationToken: token);
        if (result == null) return (0, []);
        
        var vms = result.Reports.Select(r => new EventReportListViewModel
        {
            Id = r.Id,
            ReceivedTs = r.ReceivedTs,
            UserId = r.UserId,
            RoomId = r.RoomId,
            EventId = r.EventId,
            Reason = r.Reason ?? string.Empty,
            Score = r.Score,
            Sender = r.Sender,
            CanonicalAlias = r.CanonicalAlias
        }).ToList();

        return (result.Total, vms);
    }

    public async Task DeleteEventReportAsync(string reportId)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.DeleteEventReportAsync(reportId);
    }
}
