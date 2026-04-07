namespace SynapseAdmin.Models.ViewModels;

public class RoomStatisticsViewModel
{
    public string RoomId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long EstimatedSize { get; set; }
}
