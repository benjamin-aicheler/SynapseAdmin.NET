namespace SynapseAdmin.Models.ViewModels;

public class BackgroundUpdatesStatusViewModel
{
    public bool Enabled { get; set; }
    public Dictionary<string, CurrentBackgroundUpdateViewModel> CurrentUpdates { get; set; } = [];
}

public class CurrentBackgroundUpdateViewModel
{
    public string Name { get; set; } = string.Empty;
    public long TotalItemCount { get; set; }
    public double TotalDurationMs { get; set; }
    public double AverageItemsPerMs { get; set; }
}
