using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Extensions.Mapping;

public static class EventReportMappingExtensions
{
    public static EventReportListViewModel ToViewModel(this SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport report)
    {
        return new EventReportListViewModel
        {
            Id = report.Id,
            ReceivedTs = report.ReceivedTs,
            UserId = report.UserId,
            RoomId = report.RoomId,
            EventId = report.EventId,
            Reason = report.Reason ?? string.Empty,
            Score = report.Score,
            Sender = report.Sender,
            CanonicalAlias = report.CanonicalAlias
        };
    }

    /// <summary>The report's fields; <paramref name="eventJson"/> is the reported event, already formatted.</summary>
    public static EventReportDetailViewModel ToDetailViewModel(this SynapseAdminEventReportDetailsResponse report, string eventJson)
    {
        return new EventReportDetailViewModel
        {
            Id = report.Id,
            ReceivedTs = report.ReceivedTs,
            UserId = report.UserId,
            RoomId = report.RoomId,
            RoomName = report.Name,
            CanonicalAlias = report.CanonicalAlias,
            EventId = report.EventId,
            Reason = report.Reason ?? string.Empty,
            Score = report.Score,
            Sender = report.Sender,
            EventJson = eventJson
        };
    }

    public static List<EventReportListViewModel> ToViewModels(this IEnumerable<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport> reports)
    {
        return reports.Select(ToViewModel).ToList();
    }
}
