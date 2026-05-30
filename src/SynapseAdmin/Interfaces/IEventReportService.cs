using MudBlazor;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IEventReportService
{
    Task<OperationResult<(int Total, List<EventReportListViewModel> Reports)>> GetEventReportsAsync(int offset, int limit, SortDirection direction, string? searchTerm = null, CancellationToken token = default);
    Task<OperationResult> DeleteEventReportAsync(string reportId, CancellationToken token = default);
}
