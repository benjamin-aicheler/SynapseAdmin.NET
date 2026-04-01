namespace SynapseAdmin.Models.ViewModels;

public class RoomListViewModel
{
    public string RoomId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? CanonicalAlias { get; set; }
    public int JoinedMembers { get; set; }
    public int JoinedLocalMembers { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string? Encryption { get; set; }
    public bool Federated { get; set; }
    public bool Public { get; set; }
    public string? AvatarUrl { get; set; }
    public string? JoinRules { get; set; }
    public string? RoomType { get; set; }
}
