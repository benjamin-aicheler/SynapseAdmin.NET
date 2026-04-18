using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces.Gateways;

namespace SynapseAdmin.Services;

public class EventReportService(IMatrixSessionService sessionService, ILogger<EventReportService> logger, IStringLocalizer<SharedResources> L) : IEventReportService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<(int Total, List<EventReportListViewModel> Reports)>> GetEventReportsAsync(int offset, int limit, SortDirection direction, CancellationToken token = default)
    {
        if (Gateway == null) return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Failure(L["NotAuthenticated"]);

        try
        {
            var dir = direction == SortDirection.Ascending ? "f" : "b";
            var result = await Gateway.GetEventReportListAsync(offset, limit, dir, token);
            if (result == null) return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Ok((0, []));
            
            var vms = result.Reports.ToViewModels();

            return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Ok((result.Total, vms));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching event reports (offset: {Offset}, limit: {Limit})", offset, limit);
            return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Failure(L["ErrorFetchingEventReports"]);
        }
    }

    public async Task<OperationResult> DeleteEventReportAsync(string reportId)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await Gateway.DeleteEventReportAsync(reportId);
            logger.LogInformation("Successfully deleted event report {ReportId}", reportId.SanitizeForLogging());
            return OperationResult.Ok(L["EventReportDeletedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting event report {ReportId}", reportId.SanitizeForLogging());
            return OperationResult.Failure(L["ErrorDeletingEventReport"]);
        }
    }
}
