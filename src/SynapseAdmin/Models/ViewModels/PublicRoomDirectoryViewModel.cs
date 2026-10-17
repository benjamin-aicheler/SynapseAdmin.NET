namespace SynapseAdmin.Models.ViewModels;

public class PublicRoomDirectoryViewModel
{
    public List<PublicRoomItemViewModel> Chunk { get; set; } = [];
    public string? NextBatch { get; set; }
    public string? PrevBatch { get; set; }
    public int? TotalRoomCountEstimate { get; set; }
}

public class PublicRoomItemViewModel
{
    public string? AvatarUrl { get; set; }
    public string? CanonicalAlias { get; set; }
    public bool GuestCanJoin { get; set; }
    public string? JoinRule { get; set; }
    public string? Name { get; set; }
    public int NumJoinedMembers { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public bool WorldReadable { get; set; }
}
