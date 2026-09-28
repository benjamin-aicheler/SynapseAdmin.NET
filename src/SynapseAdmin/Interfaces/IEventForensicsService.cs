using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IEventForensicsService
{
    Task<OperationResult<EventForensicsViewModel>> FetchEventAsync(string eventId, CancellationToken cancellationToken = default);
}
