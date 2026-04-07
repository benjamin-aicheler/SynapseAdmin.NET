namespace SynapseAdmin.Models.ViewModels;

public class UserMediaStatisticsViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int MediaCount { get; set; }
    public long TotalSize { get; set; }
}
