using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;

namespace SynapseAdmin.Services;

public class EventReportService(IMatrixSessionService sessionService, ILogger<EventReportService> logger, IStringLocalizer<SharedResources> L) : IEventReportService
{
    public Task<OperationResult<(int Total, List<EventReportListViewModel> Reports)>> GetEventReportsAsync(int offset, int limit, SortDirection direction, string? searchTerm = null, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingEventReports",
            ex => logger.LogError(ex, "Error fetching event reports (offset: {Offset}, limit: {Limit}, searchTerm: {SearchTerm})", offset, limit, searchTerm.SanitizeForLogging()),
            async gateway =>
            {
                var dir = direction.ToSynapseDir(whenUnsorted: "b");
                var result = await gateway.GetEventReportListAsync(offset, limit, dir, searchTerm, token);
                if (result == null) return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Ok((0, []));

                var vms = result.Reports.ToViewModels();

                return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Ok((result.Total, vms));
            });

    public Task<OperationResult> DeleteEventReportAsync(string reportId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingEventReport",
            ex => logger.LogError(ex, "Error deleting event report {ReportId}", reportId.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.DeleteEventReportAsync(reportId, token);
                logger.LogInformation("Successfully deleted event report {ReportId}", reportId.SanitizeForLogging());
                return OperationResult.Ok(L["EventReportDeletedSuccessfully"]);
            });
}
