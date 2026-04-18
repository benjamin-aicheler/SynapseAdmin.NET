using SynapseAdmin.Models.ViewModels;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;

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

    public static List<EventReportListViewModel> ToViewModels(this IEnumerable<SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport> reports)
    {
        return reports.Select(ToViewModel).ToList();
    }
}
