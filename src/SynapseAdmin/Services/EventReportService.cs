using System.Text.Json;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;

namespace SynapseAdmin.Services;

public class EventReportService(IMatrixSessionService sessionService, ILogger<EventReportService> logger, IStringLocalizer<SharedResources> L) : IEventReportService
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public Task<OperationResult<(int Total, List<EventReportListViewModel> Reports)>> GetEventReportsAsync(int offset, int limit, SortDirection direction, EventReportListFilter? filter = null, CancellationToken token = default)
    {
        var sent = Normalize(filter);
        return sessionService.RunGatewayCallAsync(L, "ErrorFetchingEventReports",
            ex => logger.LogError(ex, "Error fetching event reports (offset: {Offset}, limit: {Limit}, roomId: {RoomId}, reporter: {ReporterUserId}, sender: {SenderUserId})",
                offset, limit, sent?.RoomId.SanitizeForLogging(), sent?.ReporterUserId.SanitizeForLogging(), sent?.SenderUserId.SanitizeForLogging()),
            async gateway =>
            {
                var dir = direction.ToSynapseDir(whenUnsorted: "b");
                var result = await gateway.GetEventReportListAsync(offset, limit, dir, sent, token);
                if (result == null) return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Ok((0, []));

                var vms = result.Reports.ToViewModels();

                return OperationResult<(int Total, List<EventReportListViewModel> Reports)>.Ok((result.Total, vms));
            },
            // A page loads this by itself: a homeserver that refuses it works as intended, so the refusal isn't an error.
            logRefusal: _ => logger.LogDebug("The homeserver doesn't support event reports"));
    }

    /// <summary>
    /// The report with the reported event (#588). A report the homeserver can't show (it answers 404 when the event
    /// was purged or the room deleted) is a warning, not an error. The event JSON and the reason are never logged.
    /// </summary>
    public Task<OperationResult<EventReportDetailViewModel>> GetEventReportDetailsAsync(string reportId, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorFetchingEventReportDetails",
            ex => logger.LogError(ex, "Error fetching event report {ReportId}", reportId.SanitizeForLogging()),
            async gateway =>
            {
                var report = await gateway.GetEventReportDetailsAsync(reportId, token);
                if (report == null)
                {
                    logger.LogWarning("Event report {ReportId} not found on the homeserver", reportId.SanitizeForLogging());
                    return OperationResult<EventReportDetailViewModel>.Failure(L["EventReportDetailsNotFound"], Severity.Warning);
                }

                var eventJson = report.EventJson == null ? string.Empty : JsonSerializer.Serialize(report.EventJson, IndentedJson);
                return OperationResult<EventReportDetailViewModel>.Ok(report.ToDetailViewModel(eventJson));
            });

    /// <summary>Trims each field and drops blank ones; <c>null</c> when none is left.</summary>
    private static EventReportListFilter? Normalize(EventReportListFilter? filter)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        if (filter == null) return null;
        var cleaned = new EventReportListFilter(Clean(filter.RoomId), Clean(filter.ReporterUserId), Clean(filter.SenderUserId));
        return cleaned is { RoomId: null, ReporterUserId: null, SenderUserId: null } ? null : cleaned;
    }

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
