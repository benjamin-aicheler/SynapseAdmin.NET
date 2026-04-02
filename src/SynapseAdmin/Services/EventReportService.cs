using System.Net.Http.Json;
using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace SynapseAdmin.Services;

public class EventReportService(IMatrixSessionService sessionService, ILogger<EventReportService> logger, IStringLocalizer<SharedResources> L) : IEventReportService
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<OperationResult<(int Total, List<EventReportListViewModel> Reports)>> GetEventReportsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default)
    {
        if (SynapseAdmin == null) return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Failure(L["NotAuthenticated"]);

        try
        {
            var dir = direction == SortDirection.Ascending ? "f" : "b";
            var url = $"/_synapse/admin/v1/event_reports?from={offset}&limit={limit}&dir={dir}";

            var result = await SynapseAdmin.ClientHttpClient.GetFromJsonAsync<SynapseAdminEventReportListResult>(url, cancellationToken: token);
            if (result == null) return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Ok((0, []));
            
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

            return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Ok((result.Total, vms));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching event reports (offset: {Offset}, limit: {Limit})", offset, limit);
            return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Failure(string.Format(L["ErrorFetchingEventReports"], ex.Message));
        }
    }

    public async Task<OperationResult> DeleteEventReportAsync(string reportId)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await SynapseAdmin.Admin.DeleteEventReportAsync(reportId);
            logger.LogInformation("Successfully deleted event report {ReportId}", reportId);
            return OperationResult.Ok(L["EventReportDeletedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting event report {ReportId}", reportId);
            return OperationResult.Failure(string.Format(L["ErrorDeletingEventReport"], reportId, ex.Message));
        }
    }
}
