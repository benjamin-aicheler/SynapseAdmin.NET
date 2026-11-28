using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IEventReportService
{
    Task<OperationResult<(int Total, List<EventReportListViewModel> Reports)>> GetEventReportsAsync(int offset, int limit, SortDirection direction, EventReportListFilter? filter = null, CancellationToken token = default);
    Task<OperationResult<EventReportDetailViewModel>> GetEventReportDetailsAsync(string reportId, CancellationToken token = default);
    Task<OperationResult> DeleteEventReportAsync(string reportId, CancellationToken token = default);
}
