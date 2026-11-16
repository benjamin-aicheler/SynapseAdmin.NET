using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Models.Responses;

namespace SynapseAdmin.Extensions.Mapping;

public static class BackgroundUpdatesMappingExtensions
{
    public static BackgroundUpdatesStatusViewModel ToViewModel(this SynapseAdminBackgroundUpdatesStatusResponse status)
    {
        return new BackgroundUpdatesStatusViewModel
        {
            Enabled = status.Enabled,
            CurrentUpdates = status.CurrentUpdates?.ToDictionary(
                kvp => kvp.Key,
                kvp => new CurrentBackgroundUpdateViewModel
                {
                    Name = kvp.Value.Name,
                    TotalItemCount = kvp.Value.TotalItemCount,
                    TotalDurationMs = kvp.Value.TotalDurationMs,
                    AverageItemsPerMs = kvp.Value.AverageItemsPerMs
                }) ?? []
        };
    }
}
